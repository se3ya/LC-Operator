using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Experimental.Animations;
using UnityEngine.Playables;

namespace Operator;

internal sealed class HoldRig : MonoBehaviour
{
    private PlayerControllerB? _player;
    private PlayableGraph _graph;
    private AnimationPlayableOutput _output;
    private AnimationPlayableOutput _leftOutput;
    private float _weight;
    private float _left;

    internal static void Add(PlayerControllerB player)
    {
        Animator animator = player.playerBodyAnimator;
        if (animator == null || animator.GetComponent<HoldRig>() != null) return;
        animator.gameObject.AddComponent<HoldRig>();
    }

    private void Awake()
    {
        _player = GetComponentInParent<PlayerControllerB>();
        var animator = GetComponent<Animator>();
        AnimationClip? clip = AssetsLoader.HoldClip;
        if (_player == null || animator == null || clip == null) return;

        _graph = PlayableGraph.Create(Constants.Assets.HoldClipName);
        _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        _output = AddOutput(animator, clip, Constants.Assets.HoldClipName, Constants.Camera.HoldSortingOrder);
        AnimationClip? left = AssetsLoader.HoldLeftClip;
        if (left != null)
        {
            _leftOutput = AddOutput(animator, left, Constants.Assets.HoldLeftClipName, Constants.Camera.HoldLeftSortingOrder);
        }
        _graph.Play();
    }

    private AnimationPlayableOutput AddOutput(Animator animator, AnimationClip clip, string name, ushort order)
    {
        var playable = AnimationClipPlayable.Create(_graph, clip);
        var output = AnimationPlayableOutput.Create(_graph, name, animator);
        output.SetAnimationStreamSource(AnimationStreamSource.PreviousInputs);
        output.SetSortingOrder(order);
        output.SetSourcePlayable(playable);
        output.SetWeight(0f);
        return output;
    }

    private void Update()
    {
        if (!_graph.IsValid() || !_output.IsOutputValid() || _player == null) return;

        var held = _player.currentlyHeldObjectServer as VideoCameraItem;
        if (held == null || !held.isActiveAndEnabled || !held.isHeld || held.isPocketed || held.playerHeldBy != _player)
        {
            _weight = 0f;
            _left = 0f;
        }
        else
        {
            bool blocked = _player.isPlayerDead || _player.isClimbingLadder || _player.inSpecialInteractAnimation
                           || _player.inVehicleAnimation || _player.performingEmote;
            var sor = StartOfRound.Instance;
            bool local = sor != null && sor.localPlayerController == _player;
            float blend = blocked && local ? Constants.Camera.HoldReleaseTime : Constants.Camera.HoldBlendTime;
            _weight = Mathf.MoveTowards(_weight, blocked ? 0f : 1f, Time.deltaTime / blend);
            float open = held.ScreenOpenAmount;
            _left = open >= _left ? open : Mathf.MoveTowards(_left, open, Time.deltaTime / Constants.Camera.HoldLeftReleaseTime);
        }

        _output.SetWeight(_weight);
        if (_leftOutput.IsOutputValid()) _leftOutput.SetWeight(_weight * _left);
    }

    private void OnDestroy()
    {
        if (_graph.IsValid()) _graph.Destroy();
    }
}
