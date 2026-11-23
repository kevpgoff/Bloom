using Bloom.Core;
using Bloom.Core.Run;
using Bloom.Game.Autoload;
using Bloom.Game.Visuals;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// At a grove: what its gardeners can do for the ship, as options to choose from. Tapping an option
/// selects it and the main button confirms it through <see cref="GameRoot.Act"/>. For now the only option is
/// repairing the hull; upgrading a piece joins it when upgrades exist.
/// </summary>
public partial class GroveScreen : Control
{
    private const float OptionHeight = 150;

    private readonly List<(Button Button, NinePatchRect? Frame, Action<RunSession> Action)> _options = [];
    private int? _selected;
    private Button _confirm = null!;

    private static RunSession? Session => GameRoot.Instance.Session;

    public override void _Ready()
    {
        _confirm = GetNode<Button>("%ConfirmButton");
        _confirm.Pressed += Confirm;
        if (Session is not { State: { } run })
        {
            return;
        }

        GetNode<Label>("%Message").Text =
            "A garden habitat turns slowly in the sunlight. Its gardeners come aboard with grafts and resin.\n" +
            $"Hull {run.Hull} / {run.MaxHull}  ·  Salvage {run.Salvage}";

        int repair = GroveEngine.RepairAmount(run);
        AddOption(
            "Repair the hull",
            repair > 0
                ? $"Mend {repair} hull ({Rules.GroveRepairPercent}% of the maximum): {run.Hull} → {run.Hull + repair}."
                : "The hull is already whole, so this changes nothing.",
            s => s.Grove.Repair(s.State));

        _selected = 0;
        ShowSelection();
        GD.Print($"GroveScreen: hull {run.Hull}/{run.MaxHull}, repair {repair}.");
    }

    private void AddOption(string title, string text, Action<RunSession> action)
    {
        int index = _options.Count;
        var accent = Palette.GardenGlow;
        var button = new Button
        {
            CustomMinimumSize = new Vector2(0, OptionHeight),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FocusMode = FocusModeEnum.None,
            ClipContents = true,
        };
        var frame = BloomTheme.CreateFrameOverlay(ArtLibrary.PanelFrame, accent with { A = 0.65f });
        if (frame is not null)
        {
            button.AddChild(frame);
        }

        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        foreach (string side in new[] { "left", "top", "right", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", 18);
        }

        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
            Text = $"[b][color=#{Palette.Label(accent).ToHtml(false)}]{title}[/color][/b]\n{text}",
        };
        label.AddThemeFontSizeOverride("normal_font_size", 24);
        label.AddThemeFontSizeOverride("bold_font_size", 32);
        margin.AddChild(label);
        button.AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        button.Pressed += () =>
        {
            _selected = index;
            ShowSelection();
        };

        GetNode<VBoxContainer>("%Options").AddChild(button);
        _options.Add((button, frame, action));
    }

    private void ShowSelection()
    {
        for (int i = 0; i < _options.Count; i++)
        {
            bool selected = i == _selected;
            var (button, frame, _) = _options[i];
            var box = BloomTheme.OfferSurface(Palette.GardenGlow, selected);
            foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed" })
            {
                button.AddThemeStyleboxOverride(state, box);
            }

            button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            if (frame is not null)
            {
                frame.Modulate = selected ? Palette.Selectable : Palette.GardenGlow with { A = 0.65f };
            }
        }

        _confirm.Disabled = _selected is null;
    }

    private void Confirm()
    {
        if (_selected is int index)
        {
            GameRoot.Instance.Act(_options[index].Action);
        }
    }
}
