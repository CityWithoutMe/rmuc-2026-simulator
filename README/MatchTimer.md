# 比赛倒计时（MatchTimer）

最基本的比赛倒计时：开局 7 分钟，屏幕上显示剩余时间，到 0 停住。

**不做时间点事件。** 不会在某个时刻触发 Buff、阶段切换、比赛结束流程或任何回调。本脚本只负责倒数和把剩余时间画在屏幕上。

未改动 `PlayerMovement.cs`、`RobotAttributeManager.cs`、`RobotStat.cs`。

## 文件位置

| 文件 | 说明 |
| --- | --- |
| `Assets/Script/Match/MatchTimer.cs` | 时间管理器（挂场景即可跑） |
| `Assets/Scenes/SampleScene.unity` | 已放一个名为 `MatchTimer` 的空物体并挂上脚本 |

工程里没有 TextMeshPro 字体资源，因此显示用 Unity UGUI 的 `UnityEngine.UI.Text`。Canvas 和文字由脚本在运行时创建，不依赖场景里已有 UI。

## 怎么挂

1. 任意场景里新建空物体（例如命名为 `MatchTimer`）。
2. 把 `MatchTimer` 脚本拖到该物体上。
3. 进入 Play：脚本会自己生成 `MatchTimerCanvas` + `TimerText`，并开始倒数。

`SampleScene` 已经挂好。打开该场景直接 Play 即可。

## 7 分钟怎么改

Inspector 里改 `Duration Seconds`：

- 默认 `420`（即 `7 * 60` 秒）
- 改成 `300` 就是 5 分钟，改成 `60` 就是 1 分钟

代码里对应字段：

```csharp
[SerializeField] private float durationSeconds = 7f * 60f;
```

改完后点 Play，屏幕从新的时长开始显示（例如 `05:00`）。

## 屏幕显示规则

- 位置：屏幕顶部居中
- 格式：`MM:SS`，两位补零。例如 `07:00` → `06:59` → … → `00:00`
- 每帧用 `Time.deltaTime` 减少剩余秒数
- 到 0 后停住，不会变成负数，画面停在 `00:00`
- 文字不拦截鼠标/射线（`raycastTarget = false`），不影响操作角色

## 行为说明

- 默认自动开始倒计时（`autoStart = true`）
- 剩余时间用 `CeilToInt` 再格式化，所以开局会完整显示约 1 秒的 `07:00`，最后不足 1 秒时先显示 `00:01`，真正到 0 才变成 `00:00`

## API（仅实际存在）

只读：

- `float RemainingSeconds`：当前剩余秒数（到 0 为止，不会为负）
- `bool IsFinished`：是否已经倒数到 0

控制（可选，默认不用管）：

- `void StartTimer()`：开始/继续倒数。若已经走完（`IsFinished == true`），调用无效，需要先 `ResetTimer`
- `void Pause()`：暂停，剩余时间保持不变
- `void ResetTimer()`：剩余时间恢复为 `durationSeconds`，`IsFinished` 清为 false。若 `autoStart` 为 true 会立刻再开始倒数

Inspector：

- `durationSeconds`：总时长（秒）
- `autoStart`：Play 后是否自动开始

没有时间点回调、没有 `OnTimeReached`、没有比赛结束事件。

## 不做的事

- 不做时间点事件（到某秒触发逻辑）
- 不做阶段性比赛、Buff、结束结算
- 不改玩家移动和机器人属性相关脚本
