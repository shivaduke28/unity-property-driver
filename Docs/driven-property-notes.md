# Driven property 調査ノート

Unity 6000.3.21f1 で `Assets/PropertyDriverExperiments` の実験スクリプトを使って確認した内容。

## API

### UnityEngine.DrivenPropertyManager (internal, CoreModule)

| メソッド | 挙動 |
| --- | --- |
| `RegisterProperty(Object driver, Object target, string propertyPath)` | target の propertyPath を driver が駆動していると登録する |
| `TryRegisterProperty(driver, target, propertyPath)` | 同上。すでに駆動済みでも黙って何もしない想定 |
| `UnregisterProperty(driver, target, propertyPath)` | 1 プロパティの登録解除 |
| `UnregisterProperties(driver)` | driver が登録した全プロパティを解除 |

`Register*Partial` / `*_Injected` は内部実装用。
公開 API は `DrivenRectTransformTracker` のみで、汎用版を使うには reflection が必要。
実験では `DrivenPropertyManagerProxy` で `Delegate.CreateDelegate` にバインドしている。

### internal API へのアクセス方法

| 方法 | 評価 |
| --- | --- |
| reflection（現行の `DrivenPropertyManagerProxy`） | 採用。衝突しない。シグネチャ変更時は `GetMethod` が null になるので、その Unity バージョンでは機能を無効化して警告を出す設計にできる |
| asmdef 名を `Unity.InternalAPIEngineBridge.0XX` にして `InternalsVisibleTo` を通す | 不採用。番号は Unity 社内でパッケージごとに割り当てられている（uGUI が 004 など）。外部パッケージが取った番号を将来の Unity パッケージが使うと利用者側でコンパイルエラーになり、直すにはアセンブリ名変更（利用者の参照も全部壊れる）しかない。Unity 自身のパッケージだから成立する方法 |

### UnityEditor.DrivenPropertyManagerInternal (internal, Editor)

| メソッド | 挙動 |
| --- | --- |
| `IsDriven(Object target, string propertyPath)` | 誰かが駆動中か |
| `IsDriving(Object driver, Object target, string propertyPath)` | 指定 driver が駆動中か |
| `IsDrivingPartial(driver, target, propertyPath)` | 未検証 |

## propertyPath の指定

`SerializedObject.FindProperty` に渡すのと同じ、シリアライズ上のプロパティパス。C# のプロパティ名ではない。実測:

| 指定 | 結果 |
| --- | --- |
| MonoBehaviour の public フィールド `value` | 登録できる（シリアライズ名がそのままフィールド名） |
| Transform `m_LocalPosition` / `m_LocalScale.y` | 登録できる |
| GameObject `m_IsActive`（ActivationTrack が使う名前） | 登録できる |
| PlayableAsset のネスト `template.value` | 登録できる。`FindProperty("template.value")` も非 null |
| Transform の C# プロパティ名 `localPosition` | エラー。`DrivenPropertyManager has failed to register property "localPosition" ... because the property doesn't exist.` |
| 存在しない名前 | 同じエラー |

複合型（Vector3 など）は登録すると子要素に展開される。
`IsDriven(transform, "m_LocalPosition")` は false、`"m_LocalPosition.x"` は true になる。
`"m_LocalScale.y"` だけ登録すれば `.y` だけ true で `.x` は false。問い合わせは末端パスで行うこと。

存在しないパスでも `RegisterProperty` は例外を投げず、エラーログを出して `IsDriven` は false のまま。

## 確認できた挙動

### dirty とシリアライズ（本命）

driven 登録の効果は **「保存される値」を固定すること** であって、dirty を抑えることではない。
以下は登録なし／ありで同じ操作をして測った結果（Unity 6000.3.21f1）。

#### 素の代入（`target.value = v` / `transform.localPosition = v`）

| | 登録なし | 登録あり |
| --- | --- | --- |
| `scene.isDirty` | 立たない | 立たない |
| `IsDirty(target)`（C# フィールド代入） | 立たない | 立たない |
| `IsDirty(transform)` | 数フレーム後に立つ | 数フレーム後に立つ |
| シーン保存で書かれる値 | **代入した値** | **登録時点の値（スナップショット）** |
| `UnregisterProperties` | - | 値が登録時点に戻る。target / transform が dirty になる |

素の代入は登録の有無に関わらず `scene.isDirty` を立てない。オブジェクト単位の dirty（Transform）は立つが scene には波及しない。
つまり「Apply しても scene が汚れない」のは登録の効果ではなく、素の代入の性質。
登録の効果が出るのは保存時で、未登録なら代入した値がそのまま YAML に残り、登録済みなら登録時点の値が残る。

#### `SerializedObject` 経由の書き込み（Inspector での手編集と同じ経路）

| | 登録なし | 登録あり |
| --- | --- | --- |
| `IsDirty(target)` | 立つ | 立つ |
| `scene.isDirty` | 次フレームで立つ | 立たない |

こちらは登録で scene の dirty が抑制される。ただし Inspector のフィールドは無効化されない（下記「Inspector での表示」参照）ので、手編集でこの経路を通ることはあり得る。

#### まとめ

- 登録の本質は「登録時点の値をネイティブ側にスナップショットし、保存時にはそれを書き、解除時にそれに戻す」こと
- driven として値を扱うときは必ず Register してから書く。未登録での素の代入は Undo にも dirty にも乗らない不正な直接書き込みで、次の保存で黙って残る
- エディタ拡張として正しく値を変える経路は `SerializedObject` / `Undo.RecordObject` で、その経路なら未登録は dirty になり、登録済みはならない
- LayoutGroup / Timeline は「登録 + 素の代入」の組で使っている。駆動中の値は一時的で Undo に積まず、保存はスナップショットが担う

### Inspector での表示（UI Toolkit インスペクタ）

グレーアウトはされない。実測（ダークテーマ）:

- driven なフィールドの input 要素に USS クラス `unity-binding--driven` が付き、背景が RGBA(0.118, 0.188, 0.220) の青系になる
- `enabledInHierarchy` は true のまま。つまり編集可能
- 関連する内部型: `UnityEditor.UIElements.BindingsStyleHelpers`、`DrivenPropertyState { NotDriven, CustomDriven, AnimationAnimated, AnimationCandidate, AnimationRecording }`、`BindingExtensions.drivenUssClassName`

このクラスの付け外しは **バインドされた値が変わったとき** にしか再評価されない。

| 操作 | 表示 |
| --- | --- |
| Register のみ（値は不変） | 変わらない。2秒待っても、`InspectorWindow.Repaint()` を呼んでも変わらない |
| Register 後に Apply（値が変わる） | 数フレーム後に青くなる |
| Unregister（値がスナップショットに戻る＝値が変わる） | 数フレーム後に青が消える |

Register 直後に表示を更新したければ、値を変えるか、バインディングの再評価を別途起こす必要がある（方法は未調査）。

### IMGUI インスペクタには driven の表示が無い

`UnityEditor.CoreModule.dll` と `UnityEngine.CoreModule.dll` を Mono.Cecil で走査し、`DrivenPropertyManager` / `DrivenPropertyManagerInternal` を呼ぶ全メソッドを列挙した結果:

| 呼び出し元 | 呼ぶ API | 用途 |
| --- | --- | --- |
| `UIElements.BindingsStyleHelpers.UpdateElementStyleFromProperty` | `IsDriven` | UI Toolkit フィールドの `unity-binding--driven` スタイル |
| `PrefabUtility.IsPropertyBeingDrivenByPrefabStage` | `IsDriving` / `IsDrivingPartial` | Prefab Stage が駆動中かの判定 |
| `SceneManagement.PrefabStage.RecordPatchedPropertiesForContent` / `RefreshPatchedProperties` | `TryRegisterProperty` / `UnregisterProperties` | Prefab Stage のパッチ済みプロパティ |
| `TrailRendererInspector.SavePositionForPreview` / `RestorePositionAfterPreview` / `BeginDrivenPropertyCheck` | `TryRegisterProperty` / `UnregisterProperties` / `IsDriving` | TrailRenderer のプレビュー中の位置退避 |

表示目的で参照しているのは UI Toolkit の1か所だけ。`EditorGUI` や `TransformInspector`（IMGUI、`OnInspectorGUI`）には driven を見るコードが無いので、
**Transform の Rotation は駆動中でも青くならない**。駆動自体は `IsDriven` と保存・復元の挙動で確認できる。
IMGUI で駆動状態を見せたければ、カスタムエディタで `IsDriven` を引いて自前で描画する必要がある。

### その他

- **二重登録は許容される。** 別 driver が同じプロパティを `RegisterProperty` してもエラーは出ず、両方 `IsDriving` が true になる。片方を解除してももう片方は残る。スナップショットは最初の登録時点のもの 1 つだけ（「境界条件の実測」参照）。
- **書き込みロックではない。** `SerializedObject` 経由で driven なプロパティに書き込むとメモリ上の値は変わる。Inspector も編集可能なまま。
- **ドメインリロードを跨いで登録が残る。** OnEnable を持たない driver で登録した後に `RequestScriptReload` しても `IsDriving` は true のまま。登録はネイティブ側に保持されている。エディタ再起動を跨ぐかは未検証。
- **エディタ側から登録して書き込んでも scene は clean のまま。** `ExecuteAlways` は不要。

### スライダー値をアセットに逃がす

driver 自身のフィールド（`t`）をスライダーで編集すると、driver は driven ではない普通のシーンオブジェクトなので当然 scene が dirty になる。
そこで `t` を `SliderDriverSettings`（ScriptableObject アセット）に移し、カスタムエディタはそのアセットの `SerializedObject` を編集する。

確認結果（`t` を 0.5 → 0.9 に変更して Apply、その後保存）:

- scene: dirty にならない
- settings アセット: dirty になり、保存後の YAML は `t: 0.9`
- target の `value` / Transform: メモリ上は 9 だが、保存された scene は `value: 0`、`m_LocalPosition: {x: 0}`（登録時のスナップショット）

「スライダーで動かした結果」はアセットにだけ残り、scene の保存内容は変わらない。


### ExecuteAlways を使わない理由

初版は `[ExecuteAlways]` の `Update` で毎フレーム書き込んでいたが、
driven でない driver 自身のフィールド変更や `OnValidate` 経由の書き込みが dirty の原因になりやすく、
そもそも「scene を汚さずに値を駆動したい」という目的とずれる。
駆動は Editor 側（カスタムエディタ）に置き、Runtime の driver はデータのみにした。

## ディレクトリ構成（feature ごと）

```
Assets/PropertyDriverExperiments/
  Core/             DrivenPropertyManagerProxy, DrivenPropertyAttribute, DrivenTrack<T>, Editor/DrivenPropertyEditorProxy
  Generator/        PropertyDriver.Generator.dll（RoslynAnalyzer ラベル）
  DrivenValue/      DrivenTarget, SliderDriver, SliderDriverSettings(+.asset), Editor/SliderDriverEditor, DrivenValue* Timeline トラック一式
  MaterialColor/    MaterialColorTarget, MaterialColor* Timeline トラック一式
  FixtureRotation/  FixtureRotation, FixtureRotation* Timeline トラック一式
  DrivenValueTimeline.playable   3 feature 共用の検証用タイムライン
```

## 実験スクリプト

- `Core/DrivenPropertyManagerProxy.cs` : reflection ラッパー
- `DrivenValue/DrivenTarget.cs` : 駆動される側 (`value`, `offset`, `color`)
- `DrivenValue/SliderDriverSettings.cs` : ScriptableObject。スライダー値 `t` を scene の外に持つ
- `DrivenValue/SliderDriver.cs` : データのみ。`settings.t` を `[min, max]` に写像した `DrivenValue` を持つ
- `Core/Editor/DrivenPropertyEditorProxy.cs` : `IsDriven` / `IsDriving` ラッパー
- `DrivenValue/Editor/SliderDriverEditor.cs` : Register / Unregister / Save scene / Save assets はボタン。スライダーは settings アセットの `t` を編集し、**登録中に限り**その場で target に書き込む（未登録なら書かない）。登録状態・値の同期状態・各オブジェクトの dirty を常時表示
- `DrivenValue/SliderDriverSettings.asset` : 実験用の設定アセット（SampleScene の driver に割り当て済み）

SampleScene に `DrivenTarget` と `SliderDriver` を配置済み。

## Timeline から駆動する

Timeline は driven property を **公開 API** で扱える。reflection は不要。

### 仕組み（com.unity.timeline 1.8.13 のソースで確認）

- `TrackAsset.GatherProperties(PlayableDirector, IPropertyCollector)` を override し、`driver.AddFromName<T>(gameObject, propertyName)` で「このトラックが書くプロパティ」を申告する（`Runtime/TrackAsset.cs:1073`）
- Timeline ウィンドウがプレビューに入るとき `AnimationMode.StartAnimationMode(previewDriver)` を呼び、続けて全トラックの `GatherProperties` を呼ぶ（`Editor/State/WindowState.cs:981-1007`）
- `IPropertyCollector` の実装 `PropertyCollector` は、`previewDriver` が AnimationMode 中のときだけ `DrivenPropertyManager.RegisterProperty(previewDriver, component, name)` を呼ぶ（`Editor/Utilities/PropertyCollector.cs:205-223`）
- プレビュー終了で `AnimationMode.StopAnimationMode(previewDriver)`（`WindowState.cs:282`）。これで登録が解除され値が戻る
- `GatherProperties` が呼ばれるのはエディタのプレビュー時のみ。`Application.isPlaying` では早期 return

### 実装（`Core/DrivenTrack.cs` と `DrivenValue/`）

- `DrivenTrack<TBinding>` : 抽象トラック基底。`protected abstract string[] DrivenPropertyPaths` を派生に必ず宣言させ、`GatherProperties` でそれを `AddFromName` する
- `DrivenValueTrack : DrivenTrack<DrivenTarget>` : `DrivenPropertyPaths => { DrivenTarget.ValuePath }` と `CreateTrackMixer`

「binding の生成済みパスを全部申告する」案（generator が interface を生成し基底が全件 gather）は一度実装したが不採用。トラックが書かない `offset` までプレビュー中に凍結され、手編集が復元で消えるため。申告は mixer が書くパスに限定し、宣言を abstract で強制する。
- `DrivenValueClip` / `DrivenValueBehaviour` : clip が `value` (float) を持つ
- `DrivenValueMixerBehaviour` : `ProcessFrame` で weight 加重平均を `target.value` に素の代入。クリップ外（総 weight 0）は書かない
- 検証用に `DrivenValueTimeline.playable`（clip 2 つ: 0-2s で 2、3-5s で 8）と、SampleScene の `Director` に PlayableDirector を配置

### 実測

| 操作 | `InAnimationMode` | `IsDriven(value)` | `target.value` | `scene.isDirty` |
| --- | --- | --- | --- | --- |
| ウィンドウを開いて director をセット | false | false | 0 | false |
| `playbackControls.SetCurrentTime(1.0)` | **true** | **true** | 2（評価後） | false |
| その状態でシーン保存 | true | true | 2 | false。YAML は `value: 0` |
| `SetCurrentTime(2.5)`（クリップの隙間） | true | true | 2（前の値が残る） | false |
| `SetCurrentTime(4.0)` | true | true | 8 | false |
| `ClearTimeline()`（プレビュー終了） | false | false | **0 に戻る** | false（`IsDirty(target)` は true） |
| プレビューなしで `director.time = 4; director.Evaluate()` | false | false | 8 | false |

最後の行は要注意。プレビュー外で `PlayableDirector.Evaluate()` を呼ぶと、登録なしの素の書き込みになり、保存すれば残る。

`DrivenTrack<TBinding>` 版でも再確認済み（クリップ位置は編集で 1.48-3.48 / 3-5 に変わっている）: 時刻 2.0 で `value` 2、プレビュー中に保存しても YAML は 0、終了で 0 に戻る。
申告は `ValuePath` だけなので `IsDriven("offset.x")` は false のまま。

検証時の落とし穴: クリップの無い時刻で評価すると weight 0 で何も書かれない。クリップ位置がエディタで変わっていると、以前通ったテストが「値が書かれない」ように見える。

補足: CLI から `SetCurrentTime` しただけでは Timeline の遅延評価（`DeferredEvaluate`）がエディタ更新まで走らないため、検証では `director.Evaluate()` を明示的に呼んだ。

## MaterialPropertyBlock を Timeline から書く（driven property の枠外）

MPB の管理は MonoBehaviour 側に置き、Timeline は薄くする。

- `MaterialColor/MaterialColorTarget.cs` : `[RequireComponent(Renderer)]`。`SetColor(Color)` で最初の呼び出し時に現在の block を控えてから色を上書きし、`ResetColor()` で控えた block に戻す。`BaseColor` は上書き前の色（控えた block に無ければ `sharedMaterial` の値）。プロパティ名は `[SerializeField] string propertyName`（既定 `_BaseColor`）
- `MaterialColor/MaterialColorTrack.cs` : binding は `MaterialColorTarget`。`CreateTrackMixer` のみ
- `MaterialColorMixerBehaviour` : クリップの色を加重平均して `SetColor(Lerp(BaseColor, 平均, 総 weight))`。クリップの無い区間と `OnPlayableDestroy` で `ResetColor()`

### driven property との関係

- `MaterialPropertyBlock` は Renderer にシリアライズされない。`SerializedObject` 上に対応するパスが無いので、`GatherProperties` で申告するものが無い
- したがって書いても scene は汚れず、保存にも出ない。ただし Timeline も何も戻してくれない
- 復元は target の `ResetColor()` を mixer が呼ぶことで行う。Timeline の Text サンプル（`TextTrackMixerBehaviour`）が mixer 内でやっている「元の値を控えて戻す」を、コンポーネント側に移した形

### 実測（URP Lit、Cube「ColorCube」、クリップ red 0-2s / blue 3-5s）

| 操作 | `IsOverriding` | block の `_BaseColor` | `scene.isDirty` | material |
| --- | --- | --- | --- | --- |
| プレビュー前 | false | 無し | false | 不変 |
| 時刻 1.0 | true | 赤 | false | 不変 |
| 時刻 2.5（隙間） | false | 無し（復元） | false | 不変 |
| 時刻 4.0、この状態で保存 | true | 青。YAML に出るのは `propertyName: _BaseColor` だけ | false | 不変 |
| プレビュー終了 | false | 無し（復元） | false | 不変 |
| 直接 `SetColor(green)` → `ResetColor()` | true → false | 緑 → 無し | false | 不変 |

`Renderer.IsDirty` も material の dirty も一度も立たなかった。
プレビュー外で `director.Evaluate()` すると上書きが残るが、グラフ破棄時に `OnPlayableDestroy` → `ResetColor()` で戻る（前版で確認）。

`sharedMaterial.color` に書くとマテリアルアセットが汚れ、`renderer.material` はエディタでインスタンスをリークするので、どちらも使わない。

## 慣性のあるハードを模して Transform を駆動する（FixtureRotation）

「Timeline は Art-Net の信号、補間はハードの都合」という分担。

### 責務の分け方（最終形）

- `FixtureRotation/FixtureRotation.cs` : `[ExecuteAlways]`。`Quaternion? targetRotation` を持ち、`SetTargetRotation(q)` / `ClearTarget()` だけが入口。`LateUpdate` はターゲットがあれば `RotateTowards(…, degreesPerSecond * Time.deltaTime)`。driven 登録は一切持たない
- `FixtureRotation/FixtureRotationTrack.cs` : `GatherProperties` で binding の Transform の `m_LocalRotation` を申告する。登録と復元は Timeline のプレビュー driver がやる
- mixer : weight 最大のクリップの回転を `SetTargetRotation` に送るだけ。隙間は最後の信号をホールド。`OnPlayableDestroy` で `ClearTarget()`

登録の主体は「プレビューで Transform を一時的に動かしている側」= トラック。ハード側は信号の有無しか知らない。

`ClearTarget` が無いと、プレビュー終了で Timeline が Transform をレストに戻したあと、`LateUpdate` が古いターゲットへ回し直す。
そのときの Transform は未登録なので普通の書き込みになり、次の保存で残る。ターゲットを nullable にして「信号なし」を表現する。

### 検討して不採用にした案

- コンポーネント自身が `m_LocalRotation` を登録し `driving` フラグを持つ: ハードにプレビューの都合が混ざる
- mixer が `playable.GetTime()` の差分で `Tick` する: スクラブ時の `FrameData.deltaTime` は 0（`seekOccurred = true`）で、逆スクラブでは進まない
- レストから固定ステップで再シミュレーションして時刻の関数にする: 決定的だが実機の挙動と別物になる
- `EditorApplication.update` から `QueuePlayerLoopUpdate()` を要求して実時間差分で回す: 実測では止まっている間にフレームが回らず効いていなかった。上限なしの実時間差分は、止まって動かした瞬間に一気に飛ぶ

### 編集モードの `Time.deltaTime`（ExecuteAlways の `LateUpdate` で実測、エディタをフォーカスした状態）

| 状況 | `LateUpdate` | `Time.deltaTime` |
| --- | --- | --- |
| Timeline ウィンドウで再生 | エディタの毎フレーム呼ばれる（324 回 / 約 11 秒） | 実フレーム差分（0.013〜0.04 秒）。`unscaledDeltaTime` と一致 |
| スクラブ中 | ドラッグしている間は毎フレーム | 実フレーム差分（0.007〜0.05 秒） |
| マウスを止める・何もしない | 呼ばれない | - |
| 空白のあと最初のフレーム | 呼ばれる | `Time.maximumDeltaTime`（0.3333 秒）で頭打ち。10 秒空いても 0.3333 |
| 値が 0 のフレーム | | 無し |

つまり ExecuteAlways + `Time.deltaTime` で、再生中は Play Mode と同じ補間、スクラブ中はドラッグしている実時間ぶんだけ動き、止めると止まり、再開時の飛びは最大 0.333 秒ぶん。エディタループを自前で回す必要は無い。

### 実測（最終形）

| 操作 | `InAnimationMode` | `IsDriven(m_LocalRotation.x)` | target | euler |
| --- | --- | --- | --- | --- |
| プレビュー前 | false | false | null | レスト |
| 時刻 0.5 | true | true（Timeline の driver） | (0,90,0) | 以降 LateUpdate で回る |
| プレビュー終了 | false | false | null | レスト |

`scene.isDirty` は一度も立たなかった。IMGUI の Transform インスペクタは driven でも青くならない（別章参照）。

## Prefab との関係

検証用に `DrivenValue/DrivenTargetPrefab.prefab`（`DrivenTarget`、`value = 1`）を作り、SampleScene に `PrefabInstance` として配置。driver は `SliderDriver`。

### 1. インスタンスで駆動したとき override になるか

| 操作 | `HasPrefabInstanceAnyOverrides` | `SerializedProperty.prefabOverride` | `GetPropertyModifications` | 保存後 YAML の `m_Modifications` |
| --- | --- | --- | --- | --- |
| 登録して 5 を書く | false | false | 無し | 無し |
| その状態で保存 | false | false | 無し | 無し |
| 解除して 6 を書く（素の代入） | false（直後） | false（直後） | 無し（直後） | |
| その状態で保存 | **true** | **true** | `value=6` | `value=6` |

driven な書き込みは override にならない。素の書き込みは保存を経て override として検出される（override 判定は保存時などに遅延評価される）。

### 2. 駆動中に Apply したら何がアセットに入るか

登録して 5 を書いた状態（スナップショットは 1）で:

| 操作 | アセットの `value` |
| --- | --- |
| `PrefabUtility.ApplyPropertyOverride(value)` | 1 のまま |
| `PrefabUtility.ApplyPrefabInstance` | 1 のまま |
| `AssetDatabase.SaveAssets` | 1 のまま |

駆動中の値は override として存在しないので、Apply しても何も反映されない。Timeline プレビュー中に誤って Apply してもアセットは汚れない。

### 3. Prefab Stage で駆動したとき

`PrefabStageUtility.OpenPrefab` で開いた `prefabContentsRoot` の `DrivenTarget` に対して:

| 操作 | `stage.scene.isDirty` | `SaveAsPrefabAsset` 後のアセット |
| --- | --- | --- |
| 登録して 7 を書く | false | 1（スナップショット） |
| 解除 | false | メモリ上の値は 1 に戻る |
| 素の代入で 8 を書く | false | **8** |

Stage 内でも保存にはスナップショットが使われる。素の代入は Stage を dirty にしないが保存すれば入る（シーンと同じ性質）。

### 4. Timeline プレビュー経由（`GatherProperties` の登録）

`DrivenValueTimeline.playable` に `DrivenValueTrack`「Driven Value (Prefab)」を追加し、`PrefabInstance` の `DrivenTarget` に binding（クリップ: 0.5-2s で 3、3-5s で 9）。プレビューの結果:

| 操作 | `value` | override | 備考 |
| --- | --- | --- | --- |
| 時刻 1.0 | 3 | 無し | Timeline の driver 名義で `IsDriven` true |
| 時刻 4.0 | 9 | 無し | |
| プレビュー中に保存 | | 無し | `m_Modifications` に出ない |
| プレビュー終了 | 1 | 無し | スナップショットに戻る |

注意: トラックを追加した直後の同じフレームで `SetTimeline` してもグラフ再構築が間に合わず、次のフレームから駆動される。

### まとめ

driven 登録された値は Prefab の override 判定・Apply・Prefab Stage の保存のすべてで「存在しない」扱いになる。
Prefab インスタンスを Timeline で駆動しても、override も Apply 漏れも起きない。
Unity 自身も Prefab Stage が一時的に書き換える値を driven で除外している（IL 走査の `PrefabStage.RecordPatchedPropertiesForContent`）。

## 境界条件の実測

### Timeline の Scene Preview をオフにしたとき

`TimelineAsset.editorSettings.scenePreview = false` にすると `WindowState.GatherProperties` が早期 return し、登録は一切行われない。
その状態でスクラブすると評価自体は行われる（mixer は動く）ので、書き込みは未登録の素の書き込みになる。

| 操作 | `value` | `IsDriven` | 保存後 YAML |
| --- | --- | --- | --- |
| 時刻 2.0 | 2 | false | |
| その状態で保存 | 2 | false | **`value: 2`** |
| `ClearTimeline` | 2（戻らない） | false | |

Scene Preview を切ると、driven 前提のトラックはシーンを汚す。トラック側で `director.playableAsset` の `editorSettings.scenePreview` を見て警告する、書き込みを止める、などの対処が必要。

### driver / target が登録中に破棄されたとき

| 操作 | 結果 |
| --- | --- |
| driver（一時オブジェクト）を `DestroyImmediate` | 登録は自動で消え、値はその場でスナップショットに戻る。`IsDriven` false |
| 破棄済み driver で `UnregisterProperties` | `MissingReferenceException`（Unity の Object マーシャリングで弾かれる） |
| target を `DestroyImmediate` | `IsDriving(driver, deadTarget)` は false。driver の `UnregisterProperties` は例外なし |

driver の破棄で登録が残り続けることはない。

### Play Mode の出入り

`SliderDriver` 名義で `value` を登録（スナップショット 0）し 5 を書いた状態で Play を押す:

| 時点 | `value` | `IsDriven` |
| --- | --- | --- |
| Play Mode 中 | **0** | false |
| Edit Mode に戻った直後 | 0 | false |
| 保存後 YAML | `value: 0` | |

Play Mode に入るときのシーン状態はスナップショットで作られ、駆動中の値は Play Mode に持ち込まれない。登録も Play Mode の往復（ドメインリロード 2 回とシーン再ロード）を跨がない。
戻ったあとは未登録なので、Timeline 外の driver は登録し直す必要がある。

### 同じプロパティに 2 つの driver が登録したとき

A が 0 で登録 → 5 を書く → B が登録（このとき値は 5）→ 7 を書く:

| 操作 | `value` | 備考 |
| --- | --- | --- |
| 保存 | YAML は **0** | スナップショットは最初の登録時点 |
| A を先に解除 | 7（B が駆動中） | |
| 続けて B を解除 | **0** | 最後の解除で最初のスナップショットに戻る |
| B を先に解除（別試行） | 7（A が駆動中） | |
| 続けて A を解除 | 0 | |

スナップショットは最初に登録した時点の値 1 つだけで、後から登録した driver は新しいスナップショットを作らない。復元は最後の driver が解除されたときに起きる。解除の順序は結果に影響しない。

## propertyPath の生成（source generator）

private な serialized field のパスを手書きせず、`[DrivenProperty]` から生成する。`src/PropertyDriver.Generator/` が生成器本体。

### 前提（Unity 6000.3 公式ドキュメントと同梱バイナリで確認）

- 生成器は Microsoft.CodeAnalysis.CSharp **4.3** でビルドする。6000.3.21f1 同梱のコンパイラは 4.3.1（`Resources/Scripting/DotNetSdkRoslyn/Microsoft.CodeAnalysis.CSharp.dll`）
- netstandard2.0 のクラスライブラリ。Unity の外で `dotnet build` する
- DLL は Assets に置き、`.meta` に `RoslynAnalyzer` ラベルと全プラットフォーム無効を設定する（`Assets/PropertyDriverExperiments/Generator/PropertyDriver.Generator.dll.meta`）
- スコープ: Assets 直下（asmdef 外）なら predefined assembly 全体、asmdef のフォルダ内ならそのアセンブリと参照側に効く
- NuGetForUnity は `analyzers/dotnet/cs`（または `roslynX.Y/cs`）配下の DLL を認識して同じ meta を書く。2022.3.12f1 以降は Roslyn 4.3.0 が上限。csproj は nupkg で `analyzers/dotnet/cs` に配置する設定にしてある

### 生成内容

```csharp
public partial class DrivenTarget : MonoBehaviour
{
    [SerializeField, DrivenProperty] float value;
    [SerializeField, DrivenProperty] Vector3 offset;
}
// 生成: DrivenTarget.DrivenProperty.g.cs
public partial class DrivenTarget
{
    public const string ValuePath = "value";
    public const string OffsetPath = "offset";
    public static readonly string[] DrivenPropertyPaths = { ValuePath, OffsetPath };
}
```

定数名は先頭を大文字にしたもの（`m_` / `_` 接頭辞があれば落とす）。ネストした型とジェネリックにも対応（containing type も partial 必須）。

### 診断（recompile で実際に確認済み）

| ID | 条件 |
| --- | --- |
| PD0001 | 型（または containing type）が partial でない。これが出た型は field 検査をしない |
| PD0002 | field が static / const |
| PD0003 | private で `[SerializeField]` が無い、または `[NonSerialized]` が付いている |
| PD0004 | `m_Dup` と `_dup` のように生成名が衝突 |

`nameof` 手書きでは取れない「本当にシリアライズされるか」を PD0003 でコンパイル時に検査できるのが利点。型がシリアライズ可能かは検査していない。

### 適用結果

- `DrivenTarget` の public field を `[SerializeField, DrivenProperty] float value` の private field + `Value` プロパティに変更。シリアライズ名は `value` のまま
- SliderDriver / Timeline トラックの `"value"` 直書きは全て `DrivenTarget.ValuePath` に置き換えた

### 開発ループ

```
cd src/PropertyDriver.Generator && dotnet build -c Release
cp bin/Release/netstandard2.0/PropertyDriver.Generator.dll ../../Assets/PropertyDriverExperiments/Generator/
```

## 未検証・次の課題

- エディタ再起動後の登録状態
- driver 破棄時に登録がどうなるか
- シーンを開き直したときの登録状態（ネイティブ登録はシーン再ロードで残るのか）
- `IsDrivingPartial` の意味
- Timeline のプレビュー中に SliderDriver 側からも同じプロパティを登録したときの挙動（二重登録は許容されるが、値の復元順序は未確認）
- Timeline の Play Mode 再生（登録なし）で書いた値の扱い
- 生成器の単体テスト（Roslyn の GeneratorDriver を使った期待出力の比較）
- 配列要素のパス（`items.Array.data[0]`）の登録可否
