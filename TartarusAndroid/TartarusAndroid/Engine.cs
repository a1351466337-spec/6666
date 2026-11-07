使用Tartarus.App；// ScanService, HintService, Hint (linked from the original app)
使用塔耳塔罗斯。规则；
使用塔耳塔罗斯的幻象；

命名空间 塔耳塔罗斯安卓版;

/// <summary>Two plain-text logs: answer.log (one line per answer) and error.log (everything that went wrong).</summary>
静态的 班级 AppLog
{
静态的只读对象 Gate = 新();
静态的string _dir ="";

公共的、静态的、无效的 Init(string filesDir)
    {
_dir = System.IO.Path.Combine(filesDir，“日志”);
Directory.CreateDirectory(_dir);
    }

静态的 string AnswerPath => System.IO.Path.Combine(_dir,“answer.log”);
静态的 string ErrorPath => System.IO.Path.Combine(_dir,“error.log”);

公共的、静态的、无效的函数：Answer(string text) => Append(AnswerPath, text.Replace('\n', ' '));

公共的 静态的 无效 Error(string where, Exception? ex = 无) =>
Append(ErrorPath, ex ==“{位置}：{ex.GetType().Name}：{ex.Message}\n{ex.StackTrace}”);

/// <summary>最新的答案行（即浮动窗口中显示的内容）。</summary>
公共的、静态的字符串？LastAnswer()
    {
        锁（大门）
        {
            尝试
            {
                如果 (!File.Exists(AnswerPath)) 返回 无;
string? last = File.ReadLines(AnswerPath).LastOrDefault(l => l.Length >0);
                如果 (last == 无) 返回 无;
int i = last.IndexOf("] "，StringComparison.Ordinal）；
                返回 i >=0? last[(i +2)..] : 最后；
            }
            抓住 { 返回 无; }
        }
    }

公共的静态字符串ReadError(int maxChars =30000)
    {
锁（大门）
        {
            尝试
            {
                如果 (!File.Exists(ErrorPath)) 返回"（暂无报错）";
string all = File.ReadAllText(ErrorPath);
返回 all.Length <= maxChars? all : all[^maxChars..]；
            }
            捕获 (异常 ex) { 返回"读取日志失败: "+ ex.Message; }
        }
    }

公共的、静态的、无效的ClearError()函数
    {
锁 (Gate) { 尝试 { File.Delete(ErrorPath); } 抓住 { } }
    }

静态的 无效 Append(string path, string text)
    {
锁（门）
        {
            尝试
            {
File.AppendAllText路径, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}\n");
如果（新的FileInfo(path)的长度大于512 *1024)
                {
                    变量 lines = File.ReadAllLines(path);
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
(_scans.问题
    readonlyScanService _scans =新(ocr线程数：4);
    readonlyHintService _hints =新（播放方式：512，秒数：2.0);
可编辑沙箱？_最后一块板子；
塔耳塔罗斯。视觉。OCR。屏幕文本？_上一条文本；

    公共的 AnswerEngine() => _scans.SetGameLanguage(“zh”);

公共的、静态的框架MakeFrame(int宽度, int高度, byte[] rgba) =>
新(width, height, rgba, Tartarus.Vision.PixelFormat.Rgba32);

公共任务WarmUpAsync() => _scans.WarmUpAsync();

公共的异步 Task<string> SolveAsync(Frame frame)
    {
        ScanResult r = 等待 _scans.ScanAsync(frame);
        如果 (_scans.Problem 是 { } problem)
        {
            AppLog.Error("识别器启动失败: "+ 问题）；
            返回 "识别器启动失败";
        }
        如果 (!r.Ok && r.Pick == 无)
        {
AppLog.Error($“未识别到棋盘或选择界面（原因：{r.Reason}；截图尺寸{frame.Width}x{frame.Height}）”);
            返回 "未识别到棋盘";
        }

观测值 obs；
        bool consumeOnly = 假；
        PickRead? pick = 无;
如果 (r.Ok)
        {
变量 sb = EditableSandbox.FromScan(r);
            // The red "choose who to eat" screen spoils the panels: what the last normal screen showed stands in.
            consumeOnly = r.Text?.ConsumePick ==真实;
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
变量 text = consumeOnly? _lastText?? r.Text : r.Text;
obs = GameObservation.From(sb, text);
            如果 (!consumeOnly)
            {
_lastBoard = sb克隆();
_lastText = r.Text;
            }
        }
        否则
                         {
            pick = r.Pick!;
变量在……之下 = _lastBoard?.Clone()?? EditableSandbox.Empty
// 一条指令
