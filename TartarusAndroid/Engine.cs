using Tartarus.App;      // ScanService, HintService, Hint (linked from the original app)
using Tartarus.Rules;
using Tartarus.Vision;

namespace TartarusAndroid;

/// <summary>Two plain-text logs: answer.log (one line per answer) and error.log (everything that went wrong).</summary>
static class AppLog
{
    static readonly object Gate = new();
    static string _dir = "";

    public static void Init(string filesDir)
    {
        _dir = System.IO.Path.Combine(filesDir, "logs");
        Directory.CreateDirectory(_dir);
    }

    static string AnswerPath => System.IO.Path.Combine(_dir, "answer.log");
    static string ErrorPath => System.IO.Path.Combine(_dir, "error.log");

    public static void Answer(string text) => Append(AnswerPath, text.Replace('\n', ' '));

    public static void Error(string where, Exception? ex = null) =>
        Append(ErrorPath, ex == null ? where : $"{where}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    /// <summary>The newest answer line (what the floating window shows).</summary>
    public static string? LastAnswer()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(AnswerPath)) return null;
                string? last = File.ReadLines(AnswerPath).LastOrDefault(l => l.Length > 0);
                if (last == null) return null;
                int i = last.IndexOf("] ", StringComparison.Ordinal);
                return i >= 0 ? last[(i + 2)..] : last;
            }
            catch { return null; }
        }
    }

    public static string ReadError(int maxChars = 30000)
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(ErrorPath)) return "（暂无报错）";
                string all = File.ReadAllText(ErrorPath);
                return all.Length <= maxChars ? all : all[^maxChars..];
            }
            catch (Exception ex) { return "读取日志失败: " + ex.Message; }
        }
    }

    public static void ClearError()
    {
        lock (Gate) { try { File.Delete(ErrorPath); } catch { } }
    }

    static void Append(string path, string text)
    {
        lock (Gate)
        {
            try
            {
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}\n");
                if (new FileInfo(path).Length > 512 * 1024)
                {
                    var lines = File.ReadAllLines(path);
                    File.WriteAllLines(path, lines.Skip(lines.Length / 2));
                }
            }
            catch { }
        }
    }
}

/// <summary>
/// One screenshot in, one answer out: the original scanner reads the frame (OCR + the cell networks), the original
/// Monte Carlo search ranks the moves, and the best one is worded for the Chinese client.
/// Single shot: no run memory from the game's log, only the last Sandbox this engine itself read.
/// </summary>
sealed class AnswerEngine : IDisposable
{
    readonly ScanService _scans = new(ocrThreads: 4);
    readonly HintService _hints = new(playouts: 512, seconds: 2.0);
    EditableSandbox? _lastBoard;
    Tartarus.Vision.Ocr.ScreenText? _lastText;

    public AnswerEngine() => _scans.SetGameLanguage("zh");

    public static Frame MakeFrame(int width, int height, byte[] rgba) =>
        new(width, height, rgba, Tartarus.Vision.PixelFormat.Rgba32);

    public Task WarmUpAsync() => _scans.WarmUpAsync();

    public async Task<string> SolveAsync(Frame frame)
    {
        ScanResult r = await _scans.ScanAsync(frame);
        if (_scans.Problem is { } problem)
        {
            AppLog.Error("识别器启动失败: " + problem);
            return "识别器启动失败";
        }
        if (!r.Ok && r.Pick == null)
        {
            AppLog.Error($"没有识别到棋盘或选择界面 (原因: {r.Reason}; 截图 {frame.Width}x{frame.Height})");
            return "未识别到棋盘";
        }

        Observation obs;
        bool consumeOnly = false;
        PickRead? pick = null;
        if (r.Ok)
        {
            var sb = EditableSandbox.FromScan(r);
            // The red "choose who to eat" screen spoils the panels: what the last normal screen showed stands in.
            consumeOnly = r.Text?.ConsumePick == true;
            if (consumeOnly && _lastBoard != null)
            {
                sb.Instruments = [.. _lastBoard.Instruments];
                sb.Chromatic = _lastBoard.Chromatic;
                sb.Theme = _lastBoard.Theme ?? sb.Theme;
                sb.Days ??= _lastBoard.Days;
            }
            else
            {
                sb.Theme ??= r.ThemeIcon;
            }
            sb.MarksByInstruments();
            var text = consumeOnly ? _lastText ?? r.Text : r.Text;
            obs = GameObservation.From(sb, text);
            if (!consumeOnly)
            {
                _lastBoard = sb.Clone();
                _lastText = r.Text;
            }
        }
        else
        {
            pick = r.Pick!;
            var under = _lastBoard?.Clone() ?? EditableSandbox.Empty();
            // An instrument pick with no day on the board is a new run's first pick.
            if (pick.Kind == PickRead.Instrument && (under.Days == null || under.Days == 0))
            {
                string? theme = _lastBoard?.Theme;
                under = EditableSandbox.Empty();
                under.Theme = theme;
            }
            obs = GameObservation.FromPick(pick, under, _lastText);
        }

        var tcs = new TaskCompletionSource<Hint>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnReady(Hint h) => tcs.TrySetResult(h);
        _hints.Ready += OnReady;
        try
        {
            _hints.Clear();   // the same position again must be searched again
            _hints.Request(obs, consumeOnly);
            var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(_hints.Seconds + 8)));
            if (done != tcs.Task) return "没有算出建议";
            return Describe(tcs.Task.Result, pick);
        }
        finally
        {
            _hints.Ready -= OnReady;
        }
    }

    static string Describe(Hint h, PickRead? pick)
    {
        var ranked = h.Ranked;
        if (ranked.Count == 0) return "没有可选操作";
        var best = ranked[0];
        return Step(best.Action, h, pick) + $"（预期绮彩≈{best.Chromatic:0.0}）";
    }

    /// <summary>A move in the Chinese client's words. Cells: the rules count from 0, the screen from 1.</summary>
    static string Step(GameAction a, Hint h, PickRead? pick)
    {
        int cell = a.Arg + 1;
        switch (a.Kind)
        {
            case ActionKind.Move: return $"去{cell}号";
            case ActionKind.Ratway: return $"下水道：去{cell}号";
            case ActionKind.Consume: return h.ConsumeOnly ? $"吃{cell}号囚犯" : $"点「吃」，目标{cell}号";
            case ActionKind.Chain: return $"连吃{cell}号囚犯";
            case ActionKind.StopChain: return "点「取消」，不再吃";
            case ActionKind.Replace: return "点「换」";
            case ActionKind.End: return "点「结束」";
            case ActionKind.Hunt: return h.HuntOnly ? $"猎{cell}号囚犯" : $"点「猎」，目标{cell}号";
            case ActionKind.Reroll: return "点重新抽取";
            case ActionKind.Interrogate: return a.Arg == 0 ? "审讯室：煽动" : "审讯室：赐予";
            case ActionKind.PickTreasure:
            {
                var wanted = (Treasure)a.Arg;
                int i = pick?.Cards.FindIndex(c => GameObservation.Treasure(c.Id) == wanted) ?? -1;
                string name = wanted switch
                {
                    Treasure.ThumbRing => "三首扳指",
                    Treasure.BrassHorn => "斗争铜号",
                    Treasure.CrimsonVintage => "赤色佳酿",
                    _ => "原罪烙印",
                };
                return i >= 0 ? $"选第{i + 1}张「{name}」" : $"选「{name}」";
            }
            case ActionKind.PickInstrument:
            {
                var wanted = (Instrument)a.Arg;
                int i = pick?.Cards.FindIndex(c => c.Id != null && GameData.InstrumentById(c.Id) == wanted) ?? -1;
                return i >= 0 ? $"选第{i + 1}张刑具（{wanted}）" : $"选刑具 {wanted}";
            }
            default: return a.Kind.ToString();
        }
    }

    public void Dispose()
    {
        _hints.Dispose();
        _scans.Dispose();
    }
}
