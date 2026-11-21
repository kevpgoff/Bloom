using Bloom.Game.Autoload;
using Godot;

namespace Bloom.Game.Screens;

/// <summary>
/// Placeholder shell that switches between the empty screens. Screens only draw state and forward
/// input; game rules belong in Bloom.Core.
/// </summary>
public partial class MainScreen : Control
{
    private static readonly (string Button, string Scene)[] Screens =
    [
        ("Layout/Nav/MapButton", "res://scenes/Map.tscn"),
        ("Layout/Nav/FittingButton", "res://scenes/Fitting.tscn"),
        ("Layout/Nav/CombatButton", "res://scenes/Combat.tscn"),
    ];

    private Control _host = null!;

    public override void _Ready()
    {
        _host = GetNode<Control>("Layout/ScreenHost");
        GetNode<Label>("Layout/Status").Text = GameRoot.Instance.Status;
        foreach (var (button, scene) in Screens)
        {
            GetNode<Button>(button).Pressed += () => ShowScreen(scene);
        }

        ShowScreen(Screens[1].Scene);
    }

    private void ShowScreen(string scenePath)
    {
        foreach (var child in _host.GetChildren())
        {
            child.QueueFree();
        }

        _host.AddChild(GD.Load<PackedScene>(scenePath).Instantiate());
    }
}
