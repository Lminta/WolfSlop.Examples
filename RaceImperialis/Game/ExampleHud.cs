using System.Globalization;
using System.Numerics;
using WolfNET.Engine.UI;

namespace RaceImperialis.Game;

/// <summary>Application-owned HUD content built entirely from engine UI controls.</summary>
public sealed class ExampleHud : IDisposable
{
    private readonly bool _car;
    private readonly RaceMinimapDefinition? _route;
    private readonly UiPanel _view, _hud, _mapViewport, _timing, _speed, _resultsMask, _results;
    private readonly UiImage[] _images;
    private readonly UiCanvas _minimap, _course;
    private readonly UiText _laps, _total, _lap, _last, _gap, _speedValue, _pause, _countdown, _fps, _resultTotal;
    private readonly UiPanel _countdownPanel, _standingsBody, _lapBody;
    private readonly UiScrollView _resultScroll;
    private readonly UiButton _restart;
    private Vector2 _viewport;
    private RacePhase? _phase;
    private RaceStatus? _race;
    private float _statusClock = 100, _mapClock = 100;
    private (Vector2 Position, Vector2 Direction, Vector2? Blue, Vector2? Green)? _mapPose;
    public UiDocument Document { get; } = new() { ScaleMode = UiScaleMode.Pixels };
    public event Action? RestartRequested;

    public ExampleHud(bool car, RaceMinimapDefinition? route)
    {
        _car = car; _route = route;
        Document.Theme = new() { Font = new(Size: 16) };
        _view = Document.Root.Add(new UiPanel("view") { Style = UiStyle.Fill with { ZIndex = -10 } });
        _images = car
            ? [Image("view.cockpit", "textures/race/cockpit.png"), Image("view.wheel", "textures/race/steering-wheel.png")]
            : [Image("view.left-hand", "textures/characters/first-person-hands.png"), Image("view.right-hand", "textures/characters/first-person-hands.png")];
        foreach (var image in _images) _view.Add(image);
        _fps = Document.Root.Add(Text("fps", "", "fps-counter", new(Size: 11)));
        _fps.Style = new() { Anchor = new(0, 1), Pivot = new(0, 1), Position = new(8, -8), Padding = new(5, 3), Background = "#111923b3", CornerRadius = 3, ZIndex = 10 };

        _hud = Document.Root.Add(new UiPanel("race-hud") { ClassName = "race-minimap", Label = "Race minimap and timing",
            Visible = route != null, Style = new() { Position = new(12), Layout = UiLayoutMode.Column, Gap = 8, Width = 240 } });
        _mapViewport = _hud.Add(new UiPanel("race-map-viewport") { ClassName = "race-minimap-viewport", Role = "group", Label = "Race map viewport",
            Style = new() { Width = 240, Height = 240, Clip = true, CornerRadius = 120 } });
        _course = _mapViewport.Add(new UiCanvas("race-course") { Style = UiStyle.Fill });
        _minimap = _mapViewport.Add(new UiCanvas("race-map") { Role = "img", Label = "Nearby track and vehicle positions",
            Style = UiStyle.Fill });
        if (route != null)
        {
            var course = new UiDrawingBuilder();
            var points = route.Route.Select(point => new Vector2(point[0], point[1])).ToArray();
            course.Polyline(points, "#071018", 4).Polyline(points, "#edf4f8", 2.8f);
            foreach (var shortcut in route.Shortcuts)
                course.Polyline(shortcut.Select(point => new Vector2(point[0], point[1])), "#e6bb68", 1.8f);
            _course.Drawing = course.Build();
        }
        _timing = _hud.Add(new UiPanel("race-timing") { ClassName = "race-timing", Label = "Lap counter and race timers",
            Style = new() { Layout = UiLayoutMode.Column, Gap = 2, Width = UiLength.Percent(1) } });
        _laps = Timing("Laps", "race-laps"); _total = Timing("Total", "race-total");
        _lap = Timing("Lap", "race-lap"); _last = Timing("Last", "race-last"); _gap = Timing("Gap", "race-gap");

        _speed = Document.Root.Add(new UiPanel("speed") { ClassName = "speedometer", Label = "Speedometer", Visible = car,
            Style = new() { Anchor = new(0, 1), Pivot = new(0, 1), Position = new(12, -36), Layout = UiLayoutMode.Row, Alignment = UiAlignment.End, Gap = 8 } });
        _speedValue = _speed.Add(Text("speed-value", "0", "speedometer-value", new("ui-monospace, monospace", 64, 700, true)));
        _speed.Add(Text("speed-unit", "km/h", font: new(Size: 16)));
        _pause = Document.Root.Add(Text("pause", "Click the game to resume driving.", "car-paused", new(Size: 16)));
        _pause.Role = "status";
        _pause.Style = new() { Anchor = new(.5f), Pivot = new(.5f), Padding = new(16, 10), Background = "#111923e6", CornerRadius = 8, BorderWidth = 1, BorderColor = "#82919e", ZIndex = 20 };
        _pause.Visible = false;
        _countdownPanel = Document.Root.Add(new UiPanel("countdown") { ClassName = "race-countdown", Visible = false,
            Style = UiStyle.Fill with { ZIndex = 30 } });
        _countdown = _countdownPanel.Add(Text("countdown-value", "", "race-countdown-number", new(Size: 150, Weight: 800)));
        _countdown.Role = "status";
        _countdown.Style = new() { Anchor = new(.5f), Pivot = new(.5f), TextOutlineColor = "#111923", TextOutlineWidth = 5 };

        _resultsMask = Document.Root.Add(new UiPanel("results-mask") { ClassName = "race-results-overlay", Visible = false,
            Style = UiStyle.Fill with { Background = "#071019b8", ZIndex = 100 }, BlocksInput = true });
        _results = _resultsMask.Add(new UiPanel("results") { ClassName = "race-results", Role = "dialog", Label = "Race results",
            Style = new() { Anchor = new(.5f), Pivot = new(.5f), Width = 620, Padding = new(12), Layout = UiLayoutMode.Column,
                Gap = 6, Background = "#111923f5", CornerRadius = 10, BorderColor = "#82919e", BorderWidth = 1 } });
        _results.Add(Text("results-title", "Race finished!", font: new(Size: 24, Weight: 700))).Role = "heading";
        _resultTotal = _results.Add(Text("result-total", "", "race-result-total"));
        _resultScroll = _results.Add(new UiScrollView("result-scroll") { Style = new() { Layout = UiLayoutMode.Column, Gap = 12, Clip = true, Width = UiLength.Percent(1), Height = 320 } });
        var standings = _resultScroll.Add(new UiPanel("standings") { ClassName = "race-standings", Role = "table",
            Style = new() { Width = UiLength.Percent(1), Layout = UiLayoutMode.Column } });
        TableRow(standings, "standing-header", ["Place", "Driver", "Time"], [ .15f, .4f, .45f ], header: true);
        _standingsBody = standings.Add(new UiPanel("standing-rows") { Role = "rowgroup", Style = new() { Width = UiLength.Percent(1), Layout = UiLayoutMode.Column, Gap = 3 } });
        var lapTable = _resultScroll.Add(new UiPanel("lap-table") { ClassName = "race-lap-results", Role = "table",
            Style = new() { Width = UiLength.Percent(1), Layout = UiLayoutMode.Column } });
        _lapBody = lapTable.Add(new UiPanel("lap-rows") { Role = "rowgroup", Style = new() { Width = UiLength.Percent(1), Layout = UiLayoutMode.Column, Gap = 3 } });
        _restart = _results.Add(new UiButton("restart") { Text = "Restart race", Style = new() { Width = UiLength.Percent(1), Height = 40, CornerRadius = 6, Background = "#274f32", BorderWidth = 2 } });
        _restart.Clicked += _ => RestartRequested?.Invoke();
    }

    private static UiImage Image(string id, string source) => new(id) { Source = source };
    private static UiText Text(string id, string text, string? css = null, UiFont? font = null) => new(id)
    {
        Text = text, ClassName = css, Font = font,
        Style = new() { TextOutlineColor = "#071018", TextOutlineWidth = 2 }
    };
    private UiText Timing(string label, string css)
    {
        var row = _timing.Add(new UiPanel(css + "-row") { Style = new() { Layout = UiLayoutMode.Row, Gap = 8, Width = UiLength.Percent(1) } });
        row.Add(Text(css + "-label", label)).Style = new() { Width = 65, TextOutlineColor = "#000", TextOutlineWidth = 2 };
        return row.Add(Text(css, "—", css, new("ui-monospace, monospace", 22, 700, true)));
    }

    public void Update(GameScene scene, ExampleRenderFrame frame, Vector2 viewport, int? fps, float milliseconds)
    {
        var resized = viewport != _viewport;
        _viewport = viewport;
        var view = frame.FirstPerson;
        _view.Visible = view.Kind != FirstPersonViewKind.None;
        if (_car)
        {
            _images[0].Style = UiStyle.Fill;
            if (resized || _view.Visible && _images[1].Style.Rotation != view.WheelRotation)
                _images[1].Style = new() { Anchor = new(.42f, .86f), Pivot = new(.5f), Width = viewport.X * .28f, Height = viewport.X * .28f,
                TransformOrigin = new(.5f), Rotation = view.WheelRotation };
        }
        else for (var i = 0; i < _images.Length; i++)
        {
            var side = i == 1 ? 1f : -1f;
            var swing = view.HandMotion.X * side;
            var offset = new Vector2(swing * .007f - view.HandMotion.Z * .012f,
                swing * .018f + view.HandMotion.Y * .007f + (view.InAir ? -.04f : view.Crouching ? .03f : 0));
            var degrees = swing * 3 + view.HandMotion.Z * 2 + (view.InAir ? side * -5 : 0);
            if (i == 1) { offset -= view.HandMotion.W * new Vector2(.05f, .08f); degrees -= view.HandMotion.W * 12; }
            _images[i].SourceRect = new(.5f * i, 0, .5f, 1);
            _images[i].Style = new() { Anchor = new(.25f + .5f * i, 1.28f), Pivot = new(.5f, 1), Position = offset * viewport,
                Width = UiLength.Percent(.5f), Height = viewport.X * 1024 / 1536, TransformOrigin = new(.5f, 1), Rotation = degrees * MathF.PI / 180 };
        }
        _fps.Text = (fps?.ToString(CultureInfo.InvariantCulture) ?? "—") + " FPS";
        _speedValue.Text = scene.Player is CarObject car ? MathF.Round(car.Controller.Speed * CarSettings.MetersPerBlock * 3.6f).ToString(CultureInfo.InvariantCulture) : "0";
        if (resized) _speedValue.Font = _speedValue.Font! with { Size = Math.Clamp(viewport.Y / 12, 28, 72) };
        var phase = scene.PlayerRaceState;
        var finished = phase?.Phase == RacePhase.Finished;
        _pause.Visible = _car && scene.Paused && !finished;
        _countdownPanel.Visible = phase?.CountdownNumber != null || phase is { ShowGo: true, Phase: RacePhase.Racing };
        _countdown.Text = phase?.CountdownNumber?.ToString(CultureInfo.InvariantCulture) ?? "GO";
        _countdown.ClassName = "race-countdown-number" + (phase?.CountdownNumber == null ? " race-go" : "");
        if (resized) _countdown.Font = _countdown.Font! with { Size = Math.Clamp(viewport.Y / 4, 44, 180) };
        var fraction = phase is { Phase: RacePhase.Countdown } countdown ? (countdown.CountdownProgress * 5) % 1 : 0;
        var opacity = MathF.Max(.05f, 1 - fraction);
        var color = phase?.CountdownNumber == null ? "#79ff88" : "#ffffff";
        if (_countdown.Style.Opacity != opacity || _countdown.Style.Color != color)
            _countdown.Style = _countdown.Style with { Opacity = opacity, Color = color };
        _resultsMask.Visible = finished;
        if (finished && _phase != RacePhase.Finished) { Document.ModalRoot = _results; Document.Input.Focus(_restart); }
        _statusClock += milliseconds;
        if (_statusClock >= 100 || _phase != phase?.Phase || _race == null)
        {
            _statusClock = 0; _race = scene.RaceState; UpdateTiming();
            if (finished && _race is { } race) UpdateResults(race);
        }
        _phase = phase?.Phase;
        if (resized)
        {
            var height = Math.Clamp(viewport.Y - 170, 64, 240);
            var mapSize = MathF.Min(height, viewport.X * .35f);
            _hud.Style = _hud.Style with { Width = MathF.Max(mapSize, 170) };
            _mapViewport.Style = _mapViewport.Style with { Width = mapSize, Height = mapSize, CornerRadius = mapSize / 2 };
            var textSize = Math.Clamp(viewport.Y / 25, 12, 24);
            foreach (var text in _timing.DescendantsAndSelf().OfType<UiText>())
            {
                text.Font = new("ui-monospace, monospace", textSize, text.Font?.Weight ?? 400, text.Font?.Italic ?? false);
                if (text.Id.EndsWith("-label", StringComparison.Ordinal)) text.Style = text.Style with { Width = textSize * 3.5f };
            }
            _results.Style = _results.Style with { Width = MathF.Min(620, viewport.X - 24) };
            _resultScroll.Style = _resultScroll.Style with { Height = Math.Clamp(viewport.Y - 160, 32, 420) };
        }
        _mapClock += milliseconds;
        if (_route != null && (_mapClock >= 50 || _mapPose == null))
        {
            _mapClock = 0;
            var pose = (scene.Player.Transform.Position, scene.Player.Transform.Rotation,
                scene.Opponent?.Transform.Position, scene.SecondOpponent?.Transform.Position);
            if (_mapPose != pose) { _mapPose = pose; UpdateMap(pose.Item1, pose.Item2, pose.Item3, pose.Item4); }
        }
    }

    private void UpdateTiming()
    {
        _timing.Visible = _race != null;
        if (_race is not { } race) return;
        _laps.Text = $"{race.CompletedLaps}/{race.TotalLaps}"; _total.Text = Time(race.TotalMilliseconds);
        _lap.Text = Time(race.LapMilliseconds); _last.Text = race.LastLapMilliseconds is { } last ? Time(last) : "—";
        _gap.Parent!.Visible = race.Opponent != null;
        _gap.Text = race.GapMilliseconds is { } gap ? (gap > 0 ? "+" : "") + (gap / 1000).ToString("0.00", CultureInfo.InvariantCulture) + "s" : "—";
        _gap.ClassName = "race-gap" + (race.GapMilliseconds > 0 ? " race-gap-behind" : race.GapMilliseconds <= 0 ? " race-gap-ahead" : "");
        _gap.Style = _gap.Style with { Color = race.GapMilliseconds > 0 ? "#ff6969" : "#79ff88" };
    }
    private void UpdateMap(Vector2 position, Vector2 direction, Vector2? blue, Vector2? green)
    {
        var matrix = RaceMinimapProjection.Create(position, direction);
        _course.DrawingTransform = matrix;
        var draw = new UiDrawingBuilder();
        var finish = Vector2.Transform(new(_route!.StartFinish[0], _route.StartFinish[1]), matrix);
        draw.Transform = Matrix3x2.Identity;
        draw.Rectangle(new(finish.X - 2, finish.Y - 2, 4, 4), "#fff", "#101923", .4f);
        draw.Rectangle(new(finish.X - 2, finish.Y - 2, 2, 2), "#101923").Rectangle(new(finish.X, finish.Y, 2, 2), "#101923");
        if (blue is { } b) draw.Circle(Vector2.Transform(b, matrix), 1.8f, "#275cff", "#fff", .5f);
        if (green is { } g) draw.Circle(Vector2.Transform(g, matrix), 1.8f, "#32ed39", "#fff", .5f);
        draw.Polygon([new(50, 66), new(53, 74), new(50, 72), new(47, 74)], "#45bfff", "#f2f8ff", .5f);
        _minimap.Drawing = draw.Build();
    }
    private void UpdateResults(RaceStatus race)
    {
        _resultTotal.Text = "Total " + Time(race.TotalMilliseconds);
        _standingsBody.Children.Clear();
        foreach (var standing in race.Standings)
            TableRow(_standingsBody, "standing-" + standing.Place,
                [standing.Place.ToString(CultureInfo.InvariantCulture), standing.Name, Time(standing.Status.TotalMilliseconds) +
                    (standing.Status.Finished ? "" : $"\nRacing · {standing.Status.CompletedLaps}/{race.TotalLaps} laps")], [.15f, .4f, .45f], rowHeader: 1);
        _lapBody.Children.Clear();
        var columns = race.Opponents.Count + 2;
        var widths = Enumerable.Repeat(1f / columns, columns).ToArray();
        for (var i = 0; i < race.LapTimes.Count; i++)
            TableRow(_lapBody, "lap-result-" + i,
                new[] { (i + 1).ToString(CultureInfo.InvariantCulture), Time(race.LapTimes[i]) }.Concat(
                    race.Opponents.Select(opponent => opponent.LapTimes.Count > i ? Time(opponent.LapTimes[i]) : "—")).ToArray(), widths, rowHeader: 0);
    }
    private static void TableRow(UiElement parent, string id, string[] values, float[] widths, bool header = false, int? rowHeader = null)
    {
        var row = parent.Add(new UiPanel(id) { Role = "row", Style = new() { Layout = UiLayoutMode.Row, Width = UiLength.Percent(1) } });
        for (var i = 0; i < values.Length; i++)
            row.Add(Text(id + "-" + i, values[i], font: new(Size: header ? 12 : 14, Weight: header ? 700 : 400)))
                .ApplyCell(widths[i], header ? "columnheader" : rowHeader == i ? "rowheader" : "cell");
    }
    private static string Time(double milliseconds)
    {
        var centiseconds = (long)(milliseconds / 10);
        return string.Create(CultureInfo.InvariantCulture, $"{centiseconds / 6000}:{centiseconds / 100 % 60:00}.{centiseconds % 100:00}");
    }
    public void Dispose() { RestartRequested = null; Document.Dispose(); }
}

internal static class HudCellExtensions
{
    internal static void ApplyCell(this UiText text, float width, string role)
    {
        text.Role = role;
        text.Style = text.Style with { Width = UiLength.Percent(width), Padding = new(2, 3) };
    }
}
