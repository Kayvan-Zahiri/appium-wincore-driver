using System.Text.Json;
using WincoreServer.Commands;
using WincoreServer.State;
using WincoreServer.Uia3;
using Xunit;

namespace WincoreServer.Tests;

/// <summary>
/// setWindowRect (W3C Set Window Rect) runs server-side in one call: every value is
/// validated before the window is touched, the window is restored first, omitted values
/// keep the window's bounds as read after the restore, the size is set before the
/// position, and the resulting rect is returned. moveWindow / resizeWindow validate
/// their (required) values the same way.
/// </summary>
public class WindowRectTests
{
    private readonly SessionState _state = new();

    /// <summary>A fake top-level window: a bounding rectangle that Resize/Move/restore update.</summary>
    private sealed class FakeWindow
    {
        public FakeElement El { get; } = new();
        public List<string> Log { get; } = new();
        public tagRECT Bounds;

        /// <summary>Bounds the window takes when restored (e.g. from maximized), if set.</summary>
        public tagRECT? RestoredBounds { get; set; }

        public FakeWindow(int x, int y, int width, int height)
        {
            Bounds = Rect(x, y, width, height);
            El.Fake.Members["get_CurrentBoundingRectangle"] = _ => Bounds;

            var (window, windowFake) = UiaFake.Create<IUIAutomationWindowPattern>();
            windowFake.Members["SetWindowVisualState"] = a =>
            {
                Log.Add($"SetWindowVisualState:{a![0]}");
                if (RestoredBounds is { } restored) Bounds = restored;
                return null;
            };
            El.Patterns[UIA.WindowPatternId] = window;

            var (transform, transformFake) = UiaFake.Create<IUIAutomationTransformPattern>();
            transformFake.Members["Resize"] = a =>
            {
                var (w, h) = ((double)a![0]!, (double)a[1]!);
                Log.Add($"Resize:{w},{h}");
                Bounds = Rect(Bounds.left, Bounds.top, (int)w, (int)h);
                return null;
            };
            transformFake.Members["Move"] = a =>
            {
                var (mx, my) = ((double)a![0]!, (double)a[1]!);
                Log.Add($"Move:{mx},{my}");
                Bounds = Rect((int)mx, (int)my, Bounds.right - Bounds.left, Bounds.bottom - Bounds.top);
                return null;
            };
            El.Patterns[UIA.TransformPatternId] = transform;
        }
    }

    private static tagRECT Rect(int x, int y, int width, int height) =>
        new() { left = x, top = y, right = x + width, bottom = y + height };

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private object? SetWindowRect(FakeWindow w, string json) =>
        PatternCommands.SetWindowRect(_state, w.El.Element, Json(json));

    private static (double x, double y, double width, double height) RectOf(object? result)
    {
        var json = JsonSerializer.SerializeToElement(result);
        return (json.GetProperty("x").GetDouble(), json.GetProperty("y").GetDouble(),
            json.GetProperty("width").GetDouble(), json.GetProperty("height").GetDouble());
    }

    [Fact]
    public void AllFourValues_RestoreResizeMove_InW3COrder_AndReturnNewRect()
    {
        var w = new FakeWindow(10, 20, 300, 200);

        var result = SetWindowRect(w, """{"elementId":"1.2.3","x":100,"y":150,"width":800,"height":600}""");

        Assert.Equal(["SetWindowVisualState:Normal", "Resize:800,600", "Move:100,150"], w.Log);
        Assert.Equal((100d, 150d, 800d, 600d), RectOf(result));
    }

    [Fact]
    public void PositionOnly_DoesNotResize()
    {
        var w = new FakeWindow(10, 20, 300, 200);

        SetWindowRect(w, """{"elementId":"1.2.3","x":50,"y":75}""");

        Assert.Equal(["SetWindowVisualState:Normal", "Move:50,75"], w.Log);
    }

    [Fact]
    public void SizeOnly_DoesNotMove()
    {
        var w = new FakeWindow(10, 20, 300, 200);

        SetWindowRect(w, """{"elementId":"1.2.3","width":1024,"height":768,"x":null,"y":null}""");

        Assert.Equal(["SetWindowVisualState:Normal", "Resize:1024,768"], w.Log);
    }

    [Fact]
    public void NothingGiven_OnlyRestores_AndReturnsCurrentRect()
    {
        var w = new FakeWindow(10, 20, 300, 200);

        var result = SetWindowRect(w, """{"elementId":"1.2.3","x":null,"y":null,"width":null,"height":null}""");

        Assert.Equal(["SetWindowVisualState:Normal"], w.Log);
        Assert.Equal((10d, 20d, 300d, 200d), RectOf(result));
    }

    [Fact]
    public void SingleValues_KeepTheOtherHalfOfEachPair()
    {
        var w = new FakeWindow(10, 20, 800, 600);

        SetWindowRect(w, """{"elementId":"1.2.3","x":50,"width":1024}""");

        Assert.Equal(["SetWindowVisualState:Normal", "Resize:1024,600", "Move:50,20"], w.Log);
    }

    [Fact]
    public void OmittedValues_AreReadAfterTheRestore_NotBefore()
    {
        // Maximized at 0,0 1920x1080; restoring brings it back to 100,100 640x480.
        var w = new FakeWindow(0, 0, 1920, 1080) { RestoredBounds = Rect(100, 100, 640, 480) };

        SetWindowRect(w, """{"elementId":"1.2.3","x":5,"height":400}""");

        Assert.Equal(["SetWindowVisualState:Normal", "Resize:640,400", "Move:5,100"], w.Log);
    }

    [Fact]
    public void FractionalValues_RoundHalfUp()
    {
        var w = new FakeWindow(0, 0, 100, 100);

        SetWindowRect(w, """{"elementId":"1.2.3","x":10.4,"y":20.6,"width":800.5,"height":600.2}""");

        Assert.Equal(["SetWindowVisualState:Normal", "Resize:801,600", "Move:10,21"], w.Log);
    }

    [Fact]
    public void NegativePosition_IsAllowed()
    {
        var w = new FakeWindow(0, 0, 100, 100);

        SetWindowRect(w, """{"elementId":"1.2.3","x":-8,"y":-2147483648}""");

        Assert.Equal(["SetWindowVisualState:Normal", "Move:-8,-2147483648"], w.Log);
    }

    [Theory]
    [InlineData("""{"x":"a","y":10}""", "x must be an integer")]
    [InlineData("""{"x":10,"y":true}""", "y must be an integer")]
    [InlineData("""{"width":-1,"height":600}""", "width must be a non-negative integer")]
    [InlineData("""{"width":800,"height":-1}""", "height must be a non-negative integer")]
    [InlineData("""{"width":{},"height":600}""", "width must be a non-negative integer")]
    [InlineData("""{"x":2147483648,"y":0}""", "x must be an integer")]
    [InlineData("""{"x":2147483647.5,"y":0}""", "x must be an integer")]
    [InlineData("""{"x":0,"y":-2147483649}""", "y must be an integer")]
    [InlineData("""{"height":1e400}""", "height must be a non-negative integer")]
    public void InvalidValue_ThrowsArgumentException_BeforeTouchingTheWindow(string json, string message)
    {
        var w = new FakeWindow(10, 20, 300, 200);

        var ex = Assert.Throws<ArgumentException>(() => SetWindowRect(w, json));

        Assert.Contains(message, ex.Message);
        Assert.Empty(w.Log);
    }

    [Theory]
    [InlineData("""{"x":5}""", "y must be an integer")]
    [InlineData("""{"x":5,"y":null}""", "y must be an integer")]
    [InlineData("""{"x":"5","y":1}""", "x must be an integer")]
    public void MoveWindow_RequiresBothValidCoordinates(string json, string message)
    {
        var w = new FakeWindow(10, 20, 300, 200);

        var ex = Assert.Throws<ArgumentException>(() => PatternCommands.MoveWindow(_state, w.El.Element, Json(json)));

        Assert.Contains(message, ex.Message);
        Assert.Empty(w.Log);
    }

    [Theory]
    [InlineData("""{"width":5}""", "height must be a non-negative integer")]
    [InlineData("""{"width":-5,"height":1}""", "width must be a non-negative integer")]
    public void ResizeWindow_RequiresBothValidSizes(string json, string message)
    {
        var w = new FakeWindow(10, 20, 300, 200);

        var ex = Assert.Throws<ArgumentException>(() => PatternCommands.ResizeWindow(_state, w.El.Element, Json(json)));

        Assert.Contains(message, ex.Message);
        Assert.Empty(w.Log);
    }

    [Fact]
    public void MoveAndResizeWindow_PassValidatedIntegers()
    {
        var w = new FakeWindow(10, 20, 300, 200);

        PatternCommands.ResizeWindow(_state, w.El.Element, Json("""{"width":640.5,"height":480}"""));
        PatternCommands.MoveWindow(_state, w.El.Element, Json("""{"x":-3.5,"y":7.49}"""));

        Assert.Equal(["Resize:641,480", "Move:-3,7"], w.Log);
    }
}
