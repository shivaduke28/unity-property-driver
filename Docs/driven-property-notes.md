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

Transform の Inspector（`TransformInspector`）は IMGUI（`OnInspectorGUI`）なので上記とは別経路。driven な `m_LocalPosition` がどう表示されるかは未検証。

### その他

- **二重登録は許容される。** 別 driver が同じプロパティを `RegisterProperty` してもエラーは出ず、両方 `IsDriving` が true になる。片方を解除してももう片方は残る。
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

## 実験スクリプト

- `Runtime/DrivenPropertyManagerProxy.cs` : reflection ラッパー
- `Runtime/DrivenTarget.cs` : 駆動される側 (`value`, `offset`, `color`)
- `Runtime/SliderDriverSettings.cs` : ScriptableObject。スライダー値 `t` を scene の外に持つ
- `Runtime/SliderDriver.cs` : データのみ。`settings.t` を `[min, max]` に写像した `DrivenValue` を持つ
- `Editor/DrivenPropertyEditorProxy.cs` : `IsDriven` / `IsDriving` ラッパー
- `Editor/SliderDriverEditor.cs` : Register / Unregister / Save scene / Save assets はボタン。スライダーは settings アセットの `t` を編集し、**登録中に限り**その場で target に書き込む（未登録なら書かない）。登録状態・値の同期状態・各オブジェクトの dirty を常時表示
- `SliderDriverSettings.asset` : 実験用の設定アセット（SampleScene の driver に割り当て済み）

SampleScene に `DrivenTarget` と `SliderDriver` を配置済み。

## Timeline から駆動する

Timeline は driven property を **公開 API** で扱える。reflection は不要。

### 仕組み（com.unity.timeline 1.8.13 のソースで確認）

- `TrackAsset.GatherProperties(PlayableDirector, IPropertyCollector)` を override し、`driver.AddFromName<T>(gameObject, propertyName)` で「このトラックが書くプロパティ」を申告する（`Runtime/TrackAsset.cs:1073`）
- Timeline ウィンドウがプレビューに入るとき `AnimationMode.StartAnimationMode(previewDriver)` を呼び、続けて全トラックの `GatherProperties` を呼ぶ（`Editor/State/WindowState.cs:981-1007`）
- `IPropertyCollector` の実装 `PropertyCollector` は、`previewDriver` が AnimationMode 中のときだけ `DrivenPropertyManager.RegisterProperty(previewDriver, component, name)` を呼ぶ（`Editor/Utilities/PropertyCollector.cs:205-223`）
- プレビュー終了で `AnimationMode.StopAnimationMode(previewDriver)`（`WindowState.cs:282`）。これで登録が解除され値が戻る
- `GatherProperties` が呼ばれるのはエディタのプレビュー時のみ。`Application.isPlaying` では早期 return

### 実装（`Runtime/Timeline/`）

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

- Prefab インスタンスで driven な値が override 扱いになるか
- エディタ再起動後の登録状態
- driver 破棄時に登録がどうなるか
- シーンを開き直したときの登録状態（ネイティブ登録はシーン再ロードで残るのか）
- `IsDrivingPartial` の意味
- Timeline のプレビュー中に SliderDriver 側からも同じプロパティを登録したときの挙動（二重登録は許容されるが、値の復元順序は未確認）
- Timeline の Play Mode 再生（登録なし）で書いた値の扱い
- 生成器の単体テスト（Roslyn の GeneratorDriver を使った期待出力の比較）
- 配列要素のパス（`items.Array.data[0]`）の登録可否
