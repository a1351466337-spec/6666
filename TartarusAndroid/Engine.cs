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
    public const string NoBoard = "未识别到棋盘（事件或弹窗请先点确认）";

    ScanService _scans = new(ocrThreads: 4);
    int _fails;
    string? _pendingGuess;
    readonly List<string> _guessed = [];
    readonly HintService _hints = new(playouts: 512, seconds: 2.0);
    EditableSandbox? _lastBoard;
    Tartarus.Vision.Ocr.ScreenText? _lastText;

    public AnswerEngine() => _scans.SetGameLanguage("zh");

    public static Frame MakeFrame(int width, int height, byte[] rgba) =>
        new(width, height, rgba, Tartarus.Vision.PixelFormat.Rgba32);

    public Task WarmUpAsync() => _scans.WarmUpAsync();

    public async Task<string> SolveAsync(Frame frame, bool manual = true)
    {
        ScanResult r = await _scans.ScanAsync(frame);
        if (_scans.Problem is { } problem)
        {
            AppLog.Error("识别器启动失败: " + problem);
            return "识别器启动失败";
        }
        // The red "choose who to eat" screen: seen by its red glow at both sides, whatever the OCR made of its words.
        bool red = LooksRed(frame);
        if (!r.Ok && r.Pick == null && !(red && _lastBoard != null))
        {
            if (manual)   // the live mode looks at every screen, other apps' too: no log, no reset for those
            {
                _fails++;
                string notes = r.Notes is { } nn ? string.Join(" / ", nn) : "";
                AppLog.Error($"没有识别到棋盘或选择界面 (原因: {r.Reason}; 截图 {frame.Width}x{frame.Height}; 红屏={red}; 缩放K={r.Layout?.K:0.000}; 连续失败={_fails}; 备注={notes})");
                if (_fails % 3 == 0) ResetScanner();
            }
            return NoBoard;
        }
        _fails = 0;

        Observation obs;
        bool consumeOnly = false;
        PickRead? pick = null;
        if (r.Ok)
        {
            var sb = EditableSandbox.FromScan(r);
            // The red screen spoils the panels: what the last normal screen showed stands in.
            consumeOnly = r.Text?.ConsumePick == true || red;
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
            // The instrument icons are not always all read: the ones recommended at each pick stand in for missing ones.
            if (!consumeOnly && _pendingGuess != null)
            {
                _guessed.Add(_pendingGuess);
                _pendingGuess = null;
            }
            if (_guessed.Count > sb.Instruments.Count)
                foreach (string g in _guessed)
                    if (sb.Instruments.Count < _guessed.Count && !sb.Instruments.Contains(g)) sb.Instruments.Add(g);
            sb.MarksByInstruments();
            var text = consumeOnly ? _lastText ?? r.Text : r.Text;
            obs = GameObservation.From(sb, text);
            // The cards' own words (Chinese client): which of Consume / Replace / End are still there today.
            if (!consumeOnly && !obs.Interrogation && obs.Destinations.Count == 0 && r.Text is { } t)
            {
                var labels = t.Cards.Select(c => c.Text).ToList();
                bool eat = labels.Any(x => x.Contains('吃') || x.Contains('猎'));
                bool swap = labels.Any(x => x.Contains('换') || x.Contains('換'));
                bool end = labels.Any(x => x.Contains("结束") || x.Contains("結束"));
                if (eat || swap || end)
                {
                    obs.DecisionUsed = !eat;
                    obs.ReplaceUsed = !swap;
                }
            }
            if (!consumeOnly)
            {
                _lastBoard = sb.Clone();
                _lastText = r.Text;
            }
        }
        else if (r.Pick != null)
        {
            pick = r.Pick;
            var under = _lastBoard?.Clone() ?? EditableSandbox.Empty();
            // An instrument pick with no day on the board is a new run's first pick.
            if (pick.Kind == PickRead.Instrument && (under.Days == null || under.Days == 0))
            {
                string? theme = _lastBoard?.Theme;
                under = EditableSandbox.Empty();
                under.Theme = theme;
                _guessed.Clear();      // a new run
                _pendingGuess = null;
            }
            obs = GameObservation.FromPick(pick, under, _lastText);
        }
        else
        {
            // Red screen the scanner could not read as a Sandbox: the Sandbox stands still under it, use the last one.
            consumeOnly = true;
            var sb = _lastBoard!.Clone();
            sb.MarksByInstruments();
            obs = GameObservation.From(sb, _lastText);
        }

        AppLog.Error("[调试] " + Diag(r, obs, consumeOnly, red));

        var tcs = new TaskCompletionSource<Hint>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnReady(Hint h) => tcs.TrySetResult(h);
        _hints.Ready += OnReady;
        try
        {
            _hints.Clear();   // the same position again must be searched again
            _hints.Request(obs, consumeOnly);
            var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(_hints.Seconds + 8)));
            if (done != tcs.Task) return "没有算出建议";
            var hint = tcs.Task.Result;
            if (pick?.Kind == PickRead.Instrument && hint.Ranked.Count > 0 && hint.Ranked[0].Action.Kind == ActionKind.PickInstrument)
                _pendingGuess = GameData.Info((Instrument)hint.Ranked[0].Action.Arg).Id;
            AppLog.Error("[调试] 排名: " + string.Join(" | ", hint.Ranked.Take(5).Select(a => $"{Step(a.Action, hint, pick)}={a.Mean:0.00}")));
            return Describe(hint, pick);
        }
        finally
        {
            _hints.Ready -= OnReady;
        }
    }

    /// <summary>A fresh scanner forgets the scale and layout it settled on (after a game restart, say).</summary>
    void ResetScanner()
    {
        var old = _scans;
        _scans = new ScanService(ocrThreads: 4);
        _scans.SetGameLanguage("zh");
        old.Dispose();
        _ = _scans.WarmUpAsync();
        AppLog.Error("[调试] 连续识别失败，已重置识别器");
    }

    /// <summary>Strong red at both sides of the screen: the Consume target pick's glow.</summary>
    static bool LooksRed(Frame f)
    {
        double Share(double x0, double x1)
        {
            int total = 0, red = 0;
            int ya = (int)(f.Height * 0.15), yb = (int)(f.Height * 0.60);
            for (int y = ya; y < yb; y += 6)
                for (int x = (int)(f.Width * x0); x < (int)(f.Width * x1); x += 6)
                {
                    int i = (y * f.Width + x) * 4;
                    int pr = f.Pixels[i], pg = f.Pixels[i + 1], pb = f.Pixels[i + 2];
                    total++;
                    if (pr > 110 && pg < pr * 0.45 && pb < pr * 0.45) red++;
                }
            return total == 0 ? 0 : (double)red / total;
        }
        return Share(0.10, 0.26) > 0.08 && Share(0.74, 0.90) > 0.08;
    }

    /// <summary>One line for the error log: what the scanner read, to find out why a hint is off.</summary>
    static string Diag(ScanResult r, Observation obs, bool consumeOnly, bool red)
    {
        string cards = r.Text == null ? "" : string.Join(",", r.Text.Cards.Select(c => $"{c.Kind ?? "?"}/{c.Text}"));
        string banner = r.Text != null && r.Text.ByRole.TryGetValue("banner", out var b) ? string.Join("|", b.Select(l => l.Text)) : "";
        string cells = string.Join(" ", Enumerable.Range(1, 15).Select(i =>
        {
            var c = obs.Cells[i];
            return $"{i + 1}:{c.Kind.ToString()[0]}" + (c.Kind == ObservedKind.Inmate ? $"{c.Code.ToString()[0]}{(int)c.Rarity}" : "");
        }));
        return $"ok={r.Ok} pick={r.Pick?.Kind} red={red} consumeOnly={consumeOnly} days={obs.RemainingDays} warden={obs.Warden} " +
               $"chroma={obs.Chromatic} inst={string.Join(",", obs.Instruments)} dest={obs.Destinations.Count} interro={obs.Interrogation} " +
               $"decUsed={obs.DecisionUsed} repUsed={obs.ReplaceUsed} title={r.Text?.Title} banner={banner} cards={cards} cells={cells}";
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
                if (i >= 0 && !string.IsNullOrWhiteSpace(pick!.Cards[i].Text)) name = pick.Cards[i].Text;
                return i >= 0 ? $"选第{i + 1}张「{name}」" : $"选「{name}」";
            }
            case ActionKind.PickInstrument:
            {
                var wanted = (Instrument)a.Arg;
                int i = pick?.Cards.FindIndex(c => c.Id != null && GameData.InstrumentById(c.Id) == wanted) ?? -1;
                string label = i >= 0 && !string.IsNullOrWhiteSpace(pick!.Cards[i].Text) ? pick.Cards[i].Text : wanted.ToString();
                return i >= 0 ? $"选第{i + 1}张刑具「{label}」" : $"选刑具 {label}";
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
