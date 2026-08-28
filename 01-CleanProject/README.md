# 剑侠情缘 Mod Unity 6 干净工程

Unity 版本固定为 `6000.5.4f1`。

## 首次运行

1. 使用 Unity Hub 打开 `UnityProject`。
2. 完整导入 `JxNewResources-20260828.unitypackage`。
3. 等待资源导入和脚本编译结束。
4. 双击当前目录下的 `SetupResources.bat`。
5. 看到 `SUCCESS` 后，打开 `Assets/Scenes/main.unity` 并点击 Play。

安装脚本会检查五个资源根，拒绝资源目录中混入代码，启用 YooAsset `EditorSimulateMode`，并刷新 `DefaultPackage` 和五个 Mod/共享包的编辑器模拟清单。它不会构建真实 AssetBundle、不会生成 `StreamingAssets`，也不会打包玩家程序。

完整目录与资源包说明见仓库根目录的 `README.md` 和 `docs/RESOURCE_PACKAGE.md`。
