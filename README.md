# Unity Property Driver

Unity の driven property（`DrivenPropertyManager`）を調査し、Timeline から MonoBehaviour を「シーンを汚さずに」駆動する方法を検証するリポジトリ。
必要に応じてパッケージ化する。

## 解決したい問題

Timeline を使ったシーン制作で、Unity を実行せずにプレビューしたい。

- シーンを実行しないと反映されない → 面倒
- `[ExecuteAlways]` で反映する → 書き込んだ値がシーンに保存されてしまう

## 方針

Unity 内部の driven property を使う。登録したプロパティは、登録時点の値が保存され、解除時にその値へ戻る。
Timeline は `TrackAsset.GatherProperties` でこの仕組みに乗れる（公開 API、reflection 不要）。

## 分かったこと（要点）

詳細と実測値は [Docs/driven-property-notes.md](Docs/driven-property-notes.md)。

- driven 登録の効果は「保存される値を登録時点に固定し、解除時にそこへ戻す」こと。書き込みロックではないし、`scene.isDirty` を抑えるものでもない
- `DrivenPropertyManager` は internal。reflection でバインドする（`Unity.InternalAPIEngineBridge.*` の asmdef 名を借りる方法は外部パッケージには不適）
- propertyPath は `SerializedObject.FindProperty` と同じシリアライズ名。存在しないパスはエラーログ
- Timeline のプレビュー中は、トラックが `GatherProperties` で申告したプロパティを Timeline が登録し、終了時に戻す
- UI Toolkit のインスペクタでは driven なフィールドが青背景になる。IMGUI（Transform など）には表示が無い
- `MaterialPropertyBlock` はシリアライズされないので driven の対象外。復元はコンポーネント側で行う
- 編集モードでも `[ExecuteAlways]` の `LateUpdate` では `Time.deltaTime` に実フレーム差分が入る（`Time.maximumDeltaTime` で頭打ち）

## リポジトリ構成

```
Assets/PropertyDriverExperiments/
  Core/             DrivenPropertyManager の reflection プロキシ、[DrivenProperty] 属性、DrivenTrack<T>
  Generator/        source generator の DLL（RoslynAnalyzer ラベル）
  DrivenValue/      float 値を駆動する実験。カスタムインスペクタと Timeline トラック
  MaterialColor/    MaterialPropertyBlock で色を駆動する Timeline トラック
  FixtureRotation/  慣性のあるハードを模して Transform を駆動する Timeline トラック
  DrivenValueTimeline.playable   検証用タイムライン
Assets/Scenes/SampleScene.unity  上記を配置した検証シーン
Docs/driven-property-notes.md    調査ノート
src/PropertyDriver.Generator/    [DrivenProperty] からシリアライズパスの定数を生成する source generator
```

## source generator

`[SerializeField, DrivenProperty] float value;` のような field から、`const string ValuePath = "value"` と `string[] DrivenPropertyPaths` を partial 型に生成する。
Unity 6000.3 同梱の Roslyn 4.3 向け。ビルドと配置は以下。

```
cd src/PropertyDriver.Generator
dotnet build -c Release
cp bin/Release/netstandard2.0/PropertyDriver.Generator.dll ../../Assets/PropertyDriverExperiments/Generator/
```

## 未解決

- Material のシェーダーキーワード。Material に書くとアセットが汚れ、インスタンス生成はリークする。グローバルキーワードか、別の案か
- Prefab インスタンスで driven な値が override 扱いになるか
- エディタ再起動後に登録が残るか

## 環境

Unity 6000.3.21f1 / URP / com.unity.timeline 1.8.13
