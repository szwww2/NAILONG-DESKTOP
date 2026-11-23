# 角色模型

`nailong.blend` 是 Blender 5.0 工程，包含角色网格、材质、15 根骨骼、脸部形态键与 5 段动画。选中 `Nailong rig` 进入姿态模式可修改骨骼，选中角色网格可编辑嘴和眼睛的形态键。工程以 12 FPS 保存关键帧，Windows 桌宠会在相邻姿态之间插值。

身体经过补缝、体素重建和放松平滑。桌面网格含 26,616 个三角面，轮廓和法线与 Blender 工程一致。肩膀蒙皮沿网格平滑过渡，动画中用矫正平滑保留肘部和腹部的体积。

| 动作 | Blender 帧 |
| --- | --- |
| Idle · 待机 | 1–36 |
| Laugh · 捧腹大笑 | 37–88 |
| Feed · 喂饼干 | 89–113 |
| Sleep · 休息 | 114–149 |
| Walk · 散步 | 150–161 |

`build_model.py` 从上游模型重建工程，并导出桌宠使用的 `media/model.bin` 和 `media/model-albedo.png`。脚本读取 `media/laugh.wav` 的声强生成口型关键帧。可在 Python 3.11 环境中安装 `bpy==5.0.1` 和 `numpy==1.26.4` 后执行。重新生成会覆盖上述产物和 `nailong.blend`，请先备份手动修改的工程。

```powershell
python model/build_model.py <上游的-nailong_colored.blend-路径>
```

## 署名与使用

基础模型由 **WeDai** 制作，取自上游提交 `82bbeba708861ed55b65372bf561f73ee68cde88`。本项目增加了骨骼绑定、表情形态键、着色适配和动画，沿用上游的学习练习用途、非商业使用与保留署名要求。角色形象权利归原权利人所有。 [上游模型及版权说明](https://github.com/bigdiu/naiwa-nailoong-blender-model/tree/82bbeba708861ed55b65372bf561f73ee68cde88)
