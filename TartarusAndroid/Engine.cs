使用 Tartarus.App；// ScanService, HintService, Hint (linked from the original app)
使用 Tartarus.Rules;
使用 Tartarus.Vision；

命名空间 塔耳塔罗斯安卓版;

/// <summary>Two plain-text logs: answer.log (one line per answer) and error.log (everything that went wrong).</summary>
静态的 班级 AppLog
{
静态的只读对象 Gate = 新();
静态的 string _dir ="";

公共的、静态的、无效的 Init(string filesDir)
    {
_dir = System.IO.Path.Combine(filesDir，“日志”);
Directory.CreateDirectory(_dir);
    }

静态的 string AnswerPath => System.IO.Path.Combine(_dir,
静态的 string ErrorPath => System.IO.Path.Combine(_dir, “error.log”);

公共的、静态的、无效的。Answer(string text) => Append(AnswerPath, text.Replace('\n', ' '));

公共的 静态的 无效 Error(string where, Exception? ex = 无) =>
        Append(ErrorPath, ex ==“{位置}：{ex.GetType().Name}：{ex.Message}\n{ex.StackTrace}” ？其中：$

    /// <summary>The newest answer line (what the floating window shows).</summary>
公共的、静态的字符串？LastAnswer()
    {
        锁 （大门）
        {
            尝试
            {
                如果 (!File.Exists(AnswerPath))，则返回“无”；
string? last = File.ReadLines(AnswerPath).LastOrDefault(l => l.Length >0);
                如果 (last == 无) 返回
                int i = last.IndexOf("] "，StringComparison.Ordinal）；
                返回 i >=0? last[(i +2)..] : 最后；
            }
            抓住 { 返回 无; }
        }
    }

公共的静态字符串ReadError(int maxChars =30000)
    {
        锁 （大门）
        {
            尝试
            {
                如果 (!File.Exists(ErrorPath)) 返回"（暂无报错）";
string all = File.ReadAllText(ErrorPath);
返回 all.Length <= maxChars? all : all[^maxChars..]；
            }
抓住 (异常 ex) { 返回"读取日志失败: "+ ex.Message; }
        }
    }

公共的、静态的、无效的ClearError()函数
    {
锁 (Gate) { 尝试 { File.Delete(ErrorPath); } 抓住 { } }
    }

静态的 无效 Append(string path, string text)
    {
        锁 （大门）
        {
            尝试
            {
File.AppendAllText路径, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}\n");
如果（新的FileInfo(path)的长度大于512 *1024)
                {
                    变量 线条 = File.ReadAllLines路径);
                    File.WriteAllLines路径，lines.Skip(lines.Length /2));
                }
            }
            抓住 { }
        }
    }
}

/// <summary>
/// One screenshot in, one answer out: the original scanner reads the frame (OCR + the cell networks), the original
/// Monte Carlo search ranks the moves, and the best one is worded for the Chinese client.
/// Single shot: no run memory from the game's log, only the last Sandbox this engine itself read.
/// </summary>
密封的 班级 AnswerEngine : IDisposable
{
    readonlyScanService _scans = 新(ocrThreads:4);
只读HintService _hints = 新(playouts:512，秒数：2.0);
    可编辑沙箱？_最后一块板子；
塔耳塔罗斯。视觉。OCR。屏幕文本？_上一条文本；

公共的 AnswerEngine() => _scans.SetGameLanguage(“zh”);

公共的静态函数 Frame：MakeFrame(int宽度, int高度, byte[] rgba) =>
新（宽度，高度，rgba，Tartarus.Vision.PixelFormat.Rgba32）；

公共的 Task WarmUpAsync() => _scans.WarmUpAsync();

公共的异步Task<string> SolveAsync(Frame frame)
    {
        ScanResult r = await_scans.ScanAsync(frame);
        如果 (_scans.Problem
        {
{ }问题）"识别器启动失败: "+ 问题）；
            返回 "识别器启动失败";
        }
        如果 (!r.Ok && r.Pick == 无)
        {
AppLog.Error($“未检测到棋盘或选择界面（原因：{r.Reason}；截图尺寸 {frame.Width}x{frame.Height}）”);
            返回 "未识别到棋盘";
        }

观测值 obs；
        bool consumeOnly = 假；
        PickRead? pick = 无;
如果 (r.Ok)
        {
变量 sb = EditableSandbox.FromScan(r);
// 那个红色的“选择要吃谁”的界面会破坏画面的完整性：之前正常的画面内容会被它取代。
            consumeOnly = r.Text?.ConsumePick == 真实;
            如果（仅消费且 _lastBoard 不等于 无）
            {
sb.Instruments = [.. _lastBoard.Instruments];
sb.Chromatic = _lastBoard.Chromatic;
sb.Theme = _lastBoard.Theme?? sb.Theme;
sb.Days??= _lastBoard.Days;
            }
            否则
            {
                sb.Theme??= r.ThemeIcon;
            }
sb.MarksByInstruments();
变量 文本 = consumeOnly? _lastText?? r.Text : r.Text;
obs = GameObservation.From(sb, text);
            如果 (!仅消费)
            {
_lastBoard = sb克隆();
_lastText = r.Text;
            }
        }
        否则
        {
            pick = r.Pick!;
变量 under = _lastBoard?.Clone()?? EditableSandbox.Empty();
            // 一条指令
