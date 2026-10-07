using LethalCompanyInputUtils.Api;
using UnityEngine.InputSystem;

namespace Operator.Input;

internal sealed class OperatorInputs : LcInputActions
{
    [InputAction("<Mouse>/leftButton", Name = "Toggle Recording")]
    public InputAction ToggleRecording { get; set; } = null!;

    [InputAction("<Keyboard>/q", Name = "Zoom Out")]
    public InputAction ZoomOut { get; set; } = null!;

    [InputAction("<Keyboard>/r", Name = "Zoom In")]
    public InputAction ZoomIn { get; set; } = null!;

    [InputAction("<Keyboard>/f", Name = "Flip Camera")]
    public InputAction FlipCamera { get; set; } = null!;

    [InputAction("<Keyboard>/t", Name = "Toggle Night Vision")]
    public InputAction ToggleNightVision { get; set; } = null!;

    [InputAction("<Keyboard>/x", Name = "Eject Cassette")]
    public InputAction EjectCassette { get; set; } = null!;

    [InputAction("<Keyboard>/c", Name = "Focus Near")]
    public InputAction FocusNear { get; set; } = null!;

    [InputAction("<Keyboard>/v", Name = "Focus Far")]
    public InputAction FocusFar { get; set; } = null!;
}
