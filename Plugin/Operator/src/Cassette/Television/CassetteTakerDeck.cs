using System;
using System.Collections.Generic;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Operator.Cassette;

internal sealed class CassetteTakerDeck : MonoBehaviour
{
    private static readonly float[] Speeds = { 0.5f, 1f, 1.5f, 2f };

    private TVScript? _tv;
    private Animator? _animator;

    private InteractTrigger? _slot;
    private InteractTrigger? _startStop;
    private InteractTrigger? _pauseResume;

    private Transform? _frame1;
    private Transform? _frame2;

    private Transform? _volumeTrigger;
    private Transform? _volumeKnob;
    private Quaternion _knobRest = Quaternion.identity;

    private Transform? _volumeArrow;
    private Vector3 _arrowRestPos;
    private Quaternion _arrowRestRot = Quaternion.identity;
    private Vector3 _knobPivotLocal;
    private Vector3 _knobAxisLocal = Vector3.forward;
    private float _dialRestAngle;

    private GameObject? _visual;
    private float _slideT;
    private float _wobbleSeed;
    private Vector3 _slotMidLocal;

    private Rigidbody? _flapAnchor;
    private HingeJoint? _flap1Joint;
    private HingeJoint? _flap2Joint;
    private bool _playKeyDown;
    private bool _pauseKeyDown;
    private bool _lastLoaded;
    private float _lastSpeed = 1f;
    private int _snapshotSeen = -1;
    private bool _hasReelSpeed;
    private readonly int _reelSpeedHash = Animator.StringToHash(Constants.Television.ReelSpeedParameter);
    private readonly Dictionary<string, float> _pressedAt = new();
    private DeckDisplay? _display;
    private Material? _lampMaterial;
    private Color _lampColor;
    private bool? _lampLit;
    private int _clockKey = int.MinValue;
    private string _clockText = string.Empty;
    private AudioSource? _keySound;
    private AudioSource? _mechSound;
    private AudioSource? _motorSound;

    internal void Initialize(TVScript tv)
    {
        _tv = tv;
        _animator = GetComponent<Animator>();
        _hasReelSpeed = HasParameter(Constants.Television.ReelSpeedParameter);

        _slot = WireTrigger(Constants.Television.TriggerInsert, OnSlot, "Insert cassette : [E]");
        _startStop = WireTrigger(Constants.Television.TriggerStartStop, OnPlayStop, "Play : [E]");
        _pauseResume = WireTrigger(Constants.Television.TriggerPauseResume, OnPauseResume, "Pause : [E]");
        WireTrigger(Constants.Television.TriggerSpeedUp, OnSpeedUp, "Speed up : [E]");
        WireTrigger(Constants.Television.TriggerSpeedDown, OnSpeedDown, "Speed down : [E]");

        var volume = WireTrigger(Constants.Television.TriggerVolume, null, "Volume : [Scroll]");
        _volumeTrigger = volume != null ? volume.transform : transform.Find(Constants.Television.TriggerVolume);
        _volumeKnob = transform.Find(Constants.Television.VolumeKnobObjectName);
        if (_volumeKnob != null)
        {
            _knobRest = _volumeKnob.localRotation;
            _knobAxisLocal = transform.InverseTransformDirection(_volumeKnob.TransformDirection(Vector3.up)).normalized;

            var knobMesh = _volumeKnob.GetComponent<MeshFilter>();
            _knobPivotLocal = knobMesh != null && knobMesh.sharedMesh != null
                ? transform.InverseTransformPoint(_volumeKnob.TransformPoint(knobMesh.sharedMesh.bounds.center))
                : _volumeKnob.localPosition;

            _volumeArrow = transform.Find(Constants.Television.VolumeArrowObjectName);
            if (_volumeArrow != null)
            {
                _arrowRestPos = _volumeArrow.localPosition;
                _arrowRestRot = _volumeArrow.localRotation;
                _dialRestAngle = DialAngle(_arrowRestPos);
            }

            UpdateKnobRotation(TelevisionPlayback.LocalVolume);
        }

        _frame1 = transform.Find(Constants.Television.Frame1ObjectName);
        _frame2 = transform.Find(Constants.Television.Frame2ObjectName);
        _slotMidLocal = (_frame1 != null && _frame2 != null)
            ? transform.InverseTransformPoint((_frame1.position + _frame2.position) * 0.5f)
            : Vector3.zero;
        _wobbleSeed = UnityEngine.Random.Range(0f, 100f);
        SetupFlapPhysics();

        ApplyVanillaHoverIcon();
        RefreshTips();
        _display = DeckDisplay.Create(transform);
        CacheLamp();
        SetupSounds();
        Snap();
        UpdateDisplay();
    }

    private void ApplyVanillaHoverIcon()
    {
        if (_tv == null) return;
        var autoParent = _tv.GetComponentInParent<AutoParentToShip>();
        Transform root = autoParent != null ? autoParent.transform : _tv.transform;

        Sprite? icon = null;
        foreach (InteractTrigger t in root.GetComponentsInChildren<InteractTrigger>(true))
        {
            if (t.transform.IsChildOf(transform)) continue;
            if (t.hoverIcon != null) { icon = t.hoverIcon; break; }
        }
        if (icon == null) return;

        foreach (InteractTrigger t in GetComponentsInChildren<InteractTrigger>(true)) t.hoverIcon = icon;
    }

    private InteractTrigger? WireTrigger(string childName, UnityEngine.Events.UnityAction<PlayerControllerB>? onInteract, string hoverTip)
    {
        Transform? child = transform.Find(childName);
        if (child == null)
        {
            Plugin.Log.LogWarning($"Deck trigger '{childName}' not found on prefab.");
            return null;
        }

        var trigger = child.GetComponent<InteractTrigger>();
        if (trigger == null)
        {
            Plugin.Log.LogWarning($"'{childName}' has no InteractTrigger.");
            return null;
        }

        trigger.holdInteraction = false;
        trigger.touchTrigger = false;
        trigger.oneHandedItemAllowed = true;
        trigger.twoHandedItemAllowed = true;
        trigger.hoverTip = hoverTip;
        trigger.disabledHoverTip = string.Empty;
        trigger.interactable = true;
        trigger.onInteract.RemoveAllListeners();
        if (onInteract != null) trigger.onInteract.AddListener(onInteract);
        return trigger;
    }

    private void Update()
    {
        RefreshTips();
        if (TelevisionCoWatch.SnapshotVersion != _snapshotSeen) Snap();
        SyncKeys();
        SyncTransport();
        UpdateVolumeKnob();
        UpdateInsertVisual();
        UpdateReels();
        UpdateDisplay();
    }

    private void Snap()
    {
        _snapshotSeen = TelevisionCoWatch.SnapshotVersion;
        _lastLoaded = TelevisionCoWatch.HasLoaded;
        _lastSpeed = TelevisionCoWatch.CurrentSpeed;
        DestroyVisual();
        if (_lastLoaded)
        {
            CreateVisual(TelevisionPlayback.LoadedData);
            _slideT = 1f;
            PositionVisual(1f);
            PlayState(Constants.Television.StateLoadIn, 1f);
        }
        else
        {
            PlayState(Constants.Television.StateLoadOut, 1f);
        }

        _playKeyDown = TelevisionCoWatch.IsPlayRequested;
        _pauseKeyDown = _playKeyDown && TelevisionCoWatch.IsPaused;
        PlayState(_playKeyDown ? Constants.Television.StateStop : Constants.Television.StateStopUp, 1f);
        PlayState(_pauseKeyDown ? Constants.Television.StateResume : Constants.Television.StateResumeUp, 1f);
        if (_animator != null) _animator.Update(0f);
    }

    private void SyncTransport()
    {
        bool loaded = TelevisionCoWatch.HasLoaded;
        float speed = TelevisionCoWatch.CurrentSpeed;
        if (loaded != _lastLoaded)
        {
            _lastLoaded = loaded;
            _lastSpeed = speed;
            Echo(Constants.Television.StateEject);
            if (loaded)
            {
                CreateVisual(TelevisionPlayback.LoadedData);
                _slideT = 0f;
                KickFlaps(Constants.Television.FlapInsertOpenSpeed);
                PlayState(Constants.Television.StateLoadIn);
            }
            else
            {
                DestroyVisual();
                KickFlaps(-Constants.Television.FlapEjectOpenSpeed);
                PlayState(Constants.Television.StateLoadOut);
            }
            return;
        }

        if (Mathf.Approximately(speed, _lastSpeed)) return;
        Echo(speed > _lastSpeed ? Constants.Television.StateSpeedUp : Constants.Television.StateSpeedDown);
        _lastSpeed = speed;
    }

    private void UpdateReels()
    {
        bool advancing = TelevisionPlayback.IsAdvancing;
        float speed = TelevisionCoWatch.CurrentSpeed;
        if (_animator != null && _hasReelSpeed) _animator.SetFloat(_reelSpeedHash, advancing ? speed : 0f);
        UpdateMotor(advancing, speed);
    }

    private void SetupSounds()
    {
        DeckSounds.Init();
        AudioSource? template = _tv != null ? _tv.tvSFX : null;
        _keySound = CreateSound(Constants.Television.KeySoundObjectName, template);
        _mechSound = CreateSound(Constants.Television.MechSoundObjectName, template);
        _motorSound = CreateSound(Constants.Television.MotorSoundObjectName, template);
        _motorSound.loop = true;
        _motorSound.clip = DeckSounds.Motor;
        _motorSound.volume = Constants.Television.MotorVolume;
    }

    private AudioSource CreateSound(string name, AudioSource? template)
    {
        var holder = new GameObject(name);
        holder.transform.SetParent(transform, false);
        holder.transform.localPosition = Constants.Television.SoundLocalPosition;
        var source = holder.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.spread = 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = Constants.Television.SoundMinDistance;
        source.maxDistance = Constants.Television.SoundMaxDistance;
        if (template != null) source.outputAudioMixerGroup = template.outputAudioMixerGroup;
        return source;
    }

    private void PlaySound(string state)
    {
        switch (state)
        {
            case Constants.Television.StateLoadIn:
                PlayOneShot(_mechSound, DeckSounds.Load, Constants.Television.LoadVolume);
                break;
            case Constants.Television.StateLoadOut:
                PlayOneShot(_mechSound, DeckSounds.Eject, Constants.Television.EjectVolume);
                break;
            case Constants.Television.StateStop:
            case Constants.Television.StateStopUp:
            case Constants.Television.StateResume:
            case Constants.Television.StateResumeUp:
            case Constants.Television.StateEject:
            case Constants.Television.StateSpeedUp:
            case Constants.Television.StateSpeedDown:
                PlayKey();
                break;
        }
    }

    private void PlayKey()
    {
        AudioClip[] keys = DeckSounds.Keys;
        if (keys.Length == 0) return;
        PlayOneShot(_keySound, keys[UnityEngine.Random.Range(0, keys.Length)], Constants.Television.KeyVolume);
    }

    private static void PlayOneShot(AudioSource? source, AudioClip? clip, float volume)
    {
        if (source == null || clip == null) return;
        source.PlayOneShot(clip, volume);
        WalkieTalkie.TransmitOneShotAudio(source, clip, volume);
    }

    private void UpdateMotor(bool running, float speed)
    {
        if (_motorSound == null || _motorSound.clip == null) return;
        if (running)
        {
            _motorSound.pitch = speed;
            if (!_motorSound.isPlaying) _motorSound.Play();
        }
        else if (_motorSound.isPlaying)
        {
            _motorSound.Stop();
        }
    }

    private void UpdateDisplay()
    {
        bool powered = _tv == null || _tv.tvOn;
        SetLamp(powered);
        if (_display == null) return;
        bool loaded = TelevisionCoWatch.HasLoaded;
        bool playing = TelevisionCoWatch.IsPlayRequested;
        DeckMode mode = !powered ? DeckMode.Off
            : !loaded ? DeckMode.NoTape
            : _visual != null && _slideT < 1f ? DeckMode.Loading
            : !playing ? DeckMode.Stop
            : TelevisionCoWatch.IsPaused ? DeckMode.Pause
            : DeckMode.Play;
        float duration = loaded ? TelevisionPlayback.LoadedData.Duration : 0f;
        double position = TelevisionPlayback.Position;
        float fraction = duration > 0f ? (float)(position / duration) : 0f;
        string clock = mode == DeckMode.NoTape ? GetClockText() : string.Empty;
        _display.Show(mode, TelevisionCoWatch.CurrentSpeed, (int)position, Mathf.RoundToInt(duration), fraction, clock);
    }

    private string GetClockText()
    {
        StartOfRound sor = StartOfRound.Instance;
        TimeOfDay time = TimeOfDay.Instance;
        int key;
        if (sor.inShipPhase || !time.currentDayTimeStarted)
        {
            float blink = Constants.Television.ClockBlinkInterval;
            key = Time.unscaledTime % (blink * 2f) < blink ? -1 : -2;
        }
        else
        {
            key = (int)(time.normalizedTimeOfDay * (60f * time.numberOfHours)) + 360;
        }

        if (key == _clockKey) return _clockText;
        _clockKey = key;
        _clockText = key == -1 ? Constants.Television.IdleClock : key == -2 ? string.Empty : FormatClock(key);
        return _clockText;
    }

    private static string FormatClock(int minutes)
    {
        int hours = minutes / 60;
        if (hours >= 24) return "12:00 A";
        string half = hours < 12 ? "A" : "P";
        if (hours > 12) hours %= 12;
        return $"{hours}:{minutes % 60:00} {half}";
    }

    private void CacheLamp()
    {
        Transform? lamp = transform.Find(Constants.Television.LampObjectName);
        MeshRenderer? renderer = lamp != null ? lamp.GetComponent<MeshRenderer>() : null;
        if (renderer == null || renderer.sharedMaterial == null) return;
        if (!renderer.sharedMaterial.HasProperty(Constants.Television.LampEmissiveColor)) return;
        _lampMaterial = renderer.material;
        _lampColor = _lampMaterial.GetColor(Constants.Television.LampEmissiveColor);
    }

    private void SetLamp(bool lit)
    {
        if (_lampMaterial == null || _lampLit == lit) return;
        _lampLit = lit;
        _lampMaterial.SetColor(Constants.Television.LampEmissiveColor, lit ? _lampColor : Color.black);
    }

    private void SyncKeys()
    {
        bool playing = TelevisionCoWatch.IsPlayRequested;
        if (playing != _playKeyDown)
        {
            _playKeyDown = playing;
            PlayState(playing ? Constants.Television.StateStop : Constants.Television.StateStopUp);
        }

        bool paused = playing && TelevisionCoWatch.IsPaused;
        if (paused != _pauseKeyDown)
        {
            _pauseKeyDown = paused;
            PlayState(paused ? Constants.Television.StateResume : Constants.Television.StateResumeUp);
        }
    }

    private void RefreshTips()
    {
        if (_slot != null)
        {
            bool loaded = TelevisionCoWatch.HasLoaded;
            _slot.interactable = loaded || LocalHeldCassette() != null;
            _slot.hoverTip = loaded ? "Eject cassette : [E]" : "Insert cassette : [E]";
            _slot.disabledHoverTip = "Hold a cassette to insert";
        }
        if (_startStop != null)
        {
            _startStop.hoverTip = TelevisionCoWatch.IsPlayRequested || TelevisionCoWatch.IsPreparing ? "Stop : [E]" : "Play : [E]";
        }
        if (_pauseResume != null)
        {
            _pauseResume.hoverTip = TelevisionCoWatch.IsPaused ? "Resume : [E]" : "Pause : [E]";
        }
    }

    private void OnSlot(PlayerControllerB player)
    {
        Press(Constants.Television.StateEject);

        if (TelevisionCoWatch.HasLoaded)
        {
            TelevisionCoWatch.RequestEject();
            return;
        }

        if (player == null) return;
        CassetteItem? cassette = player.currentlyHeldObjectServer as CassetteItem;
        if (cassette == null)
        {
            return;
        }
        if (string.IsNullOrEmpty(cassette.FileName))
        {
            return;
        }

        var data = new CassetteData
        {
            Id = cassette.CassetteId,
            FileName = cassette.FileName,
            Timestamp = cassette.Timestamp,
            Duration = cassette.Duration,
            Value = cassette.scrapValue,
            Breakdown = cassette.BreakdownRaw,
        };

        Plugin.Log.LogDebug($"Loading '{data.FileName}' into deck.");
        TelevisionCoWatch.RequestInsert(data);

        if (player.IsOwner)
        {
            try { player.DestroyItemInSlotAndSync(player.currentItemSlot); }
            catch (Exception ex) { Plugin.Log.LogWarning($"Failed to remove inserted cassette: {ex.Message}"); }
        }
    }

    private void OnPlayStop(PlayerControllerB player)
    {
        if (!TelevisionCoWatch.HasLoaded) return;
        if (TelevisionCoWatch.IsPlayRequested || TelevisionCoWatch.IsPreparing) TelevisionCoWatch.RequestStop();
        else TelevisionCoWatch.RequestPlay();
    }

    private void OnPauseResume(PlayerControllerB player)
    {
        if (!TelevisionCoWatch.IsPlayRequested) return;
        TelevisionCoWatch.RequestPause(!TelevisionCoWatch.IsPaused);
    }

    private void OnSpeedUp(PlayerControllerB player)
    {
        Press(Constants.Television.StateSpeedUp);
        TelevisionCoWatch.RequestSpeed(StepSpeed(+1));
    }

    private void OnSpeedDown(PlayerControllerB player)
    {
        Press(Constants.Television.StateSpeedDown);
        TelevisionCoWatch.RequestSpeed(StepSpeed(-1));
    }

    private static float StepSpeed(int dir)
    {
        float cur = TelevisionCoWatch.CurrentSpeed;
        int idx = 0;
        float best = float.MaxValue;
        for (int i = 0; i < Speeds.Length; i++)
        {
            float d = Mathf.Abs(Speeds[i] - cur);
            if (d < best) { best = d; idx = i; }
        }
        idx = Mathf.Clamp(idx + dir, 0, Speeds.Length - 1);
        return Speeds[idx];
    }

    private void UpdateVolumeKnob()
    {
        if (_volumeKnob == null || _volumeTrigger == null) return;

        float scrollY = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
        if (Mathf.Abs(scrollY) < 0.01f) return;

        PlayerControllerB? local = GameNetworkManager.Instance.localPlayerController;
        Camera? cam = local != null ? local.gameplayCamera : null;
        if (cam == null) return;

        if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 4f, 1 << 9, QueryTriggerInteraction.Collide)) return;
        if (hit.collider == null || hit.collider.transform != _volumeTrigger) return;

        float v = Mathf.Clamp(TelevisionPlayback.LocalVolume + (Mathf.Sign(scrollY) * Constants.Television.VolumeStep),
            Constants.Television.VolumeMin, 1f);
        TelevisionPlayback.SetLocalVolume(v);
        UpdateKnobRotation(v);
    }

    private float DialAngle(Vector3 localPoint)
    {
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, _knobAxisLocal).normalized;
        Vector3 right = Vector3.Cross(up, _knobAxisLocal).normalized;
        Vector3 offset = Vector3.ProjectOnPlane(localPoint - _knobPivotLocal, _knobAxisLocal);
        return Mathf.Atan2(Vector3.Dot(offset, right), Vector3.Dot(offset, up)) * Mathf.Rad2Deg;
    }

    private void UpdateKnobRotation(float volume)
    {
        if (_volumeKnob == null) return;

        float steps = (Mathf.Clamp(volume, Constants.Television.VolumeMin, 1f) - Constants.Television.VolumeMin)
                    / Constants.Television.VolumeStep;
        float target = Constants.Television.VolumeMinAngle - (Constants.Television.VolumeStepAngle * steps);
        Quaternion turn = Quaternion.AngleAxis(_dialRestAngle - target, _knobAxisLocal);
        _volumeKnob.localRotation = turn * _knobRest;

        if (_volumeArrow == null) return;
        _volumeArrow.SetLocalPositionAndRotation(
            _knobPivotLocal + (turn * (_arrowRestPos - _knobPivotLocal)),
            turn * _arrowRestRot);
    }

    private void UpdateInsertVisual()
    {
        if (_visual == null) return;

        _slideT = Mathf.MoveTowards(_slideT, 1f, Time.deltaTime / Mathf.Max(0.05f, Constants.Television.InsertSlideTime));
        PositionVisual(_slideT);
    }

    private void CreateVisual(CassetteData data)
    {
        GameObject? src = CassetteRegistrar.CassettePrefab;
        if (src == null) return;

        var holder = new GameObject("TvDeckVisualHolder");
        holder.SetActive(false);
        GameObject clone = Instantiate(src, holder.transform);
        StripToVisual(clone);
        clone.transform.SetParent(transform, worldPositionStays: false);
        clone.transform.localScale = Constants.Television.VisualLocalScale;
        clone.name = "TvDeckCassetteVisual";
        CassetteShellColor.Apply(clone.transform, data.Id, data.Value);
        clone.SetActive(true);
        Destroy(holder);
        _visual = clone;
    }

    private static void StripToVisual(GameObject clone)
    {
        foreach (var mb in clone.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);
        foreach (var col in clone.GetComponentsInChildren<Collider>(true)) DestroyImmediate(col);
        foreach (var au in clone.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(au);
        foreach (var rb in clone.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
        foreach (var no in clone.GetComponentsInChildren<Unity.Netcode.NetworkObject>(true)) DestroyImmediate(no);
    }

    private void DestroyVisual()
    {
        if (_visual != null) Destroy(_visual);
        _visual = null;
        _slideT = 0f;
    }

    private void PositionVisual(float t)
    {
        if (_visual == null) return;
        GetSlotGeometry(out Vector3 mid, out Vector3 outward, out Quaternion rot);
        Vector3 outsidePos = mid + (outward * Constants.Television.SlotOutsideOffset);
        Vector3 insidePos = mid - (outward * Constants.Television.SlotInsideDepth);

        float draw = Settle(t, Constants.Television.InsertDrawStart, Constants.Television.InsertDrawEnd);
        float drop = Settle(t, Constants.Television.InsertDropStart, Constants.Television.InsertDropEnd);

        Vector3 pos = Vector3.LerpUnclamped(outsidePos, insidePos, draw);
        pos -= transform.up * (Constants.Television.BasketDrop * drop);

        float running = draw * (1f - draw) * 4f;
        float buzz = Mathf.Sin((Time.time + _wobbleSeed) * Constants.Television.InsertBuzzRate) * Constants.Television.InsertBuzzAmplitude * running;
        pos += transform.up * buzz;

        _visual.transform.SetPositionAndRotation(pos, rot);
    }

    private static float Settle(float t, float start, float end)
    {
        float x = Mathf.Clamp01((t - start) / Mathf.Max(0.001f, end - start));
        return x * x * (3f - (2f * x));
    }

    private void SetupFlapPhysics()
    {
        try
        {
            var anchorGo = new GameObject("FlapAnchor");
            anchorGo.transform.SetParent(transform, false);
            anchorGo.transform.localPosition = Vector3.zero;
            _flapAnchor = anchorGo.AddComponent<Rigidbody>();
            _flapAnchor.isKinematic = true;
            _flapAnchor.useGravity = false;

            _flap1Joint = BuildFlap(_frame1, +1f);
            _flap2Joint = BuildFlap(_frame2, -1f);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogDebug($"Failed to set up deck flaps: {ex.Message}");
        }
    }

    private HingeJoint? BuildFlap(Transform? flap, float dirSign)
    {
        if (flap == null || _flapAnchor == null) return null;

        var pivot = new GameObject(flap.name + "_flapPivot").transform;
        pivot.SetParent(transform, worldPositionStays: false);
        pivot.SetPositionAndRotation(flap.position, flap.rotation);

        flap.SetParent(pivot, worldPositionStays: true);

        var rb = pivot.gameObject.AddComponent<Rigidbody>();
        rb.mass = Constants.Television.FlapMass;
        rb.angularDrag = Constants.Television.FlapAngularDrag;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        var hinge = pivot.gameObject.AddComponent<HingeJoint>();
        hinge.connectedBody = _flapAnchor;
        hinge.autoConfigureConnectedAnchor = true;
        hinge.axis = Constants.Television.FlapHingeAxis * dirSign;
        hinge.anchor = Vector3.zero;
        hinge.useLimits = true;
        hinge.limits = new JointLimits { min = Constants.Television.FlapHingeMinAngle, max = Constants.Television.FlapHingeMaxAngle };
        hinge.useSpring = true;
        hinge.spring = new JointSpring
        {
            spring = Constants.Television.FlapHingeSpring,
            damper = Constants.Television.FlapHingeDamper,
            targetPosition = 0f,
        };
        return hinge;
    }

    private void KickFlaps(float openSpeedRadPerSec)
    {
        ApplyFlapKick(_flap1Joint, openSpeedRadPerSec);
        ApplyFlapKick(_flap2Joint, openSpeedRadPerSec);
    }

    private static void ApplyFlapKick(HingeJoint? joint, float openSpeedRadPerSec)
    {
        if (joint == null) return;
        var rb = joint.GetComponent<Rigidbody>();
        if (rb == null || rb.isKinematic) return;
        Vector3 axisWorld = joint.transform.TransformDirection(joint.axis).normalized;
        rb.angularVelocity = axisWorld * openSpeedRadPerSec;
    }

    private void GetSlotGeometry(out Vector3 mid, out Vector3 outward, out Quaternion rot)
    {
        mid = transform.TransformPoint(_slotMidLocal);
        outward = transform.forward;
        rot = Quaternion.LookRotation(transform.up, transform.forward);
    }

    internal void GetSlotPose(out Vector3 pos, out Quaternion rot, out Vector3 dir)
    {
        GetSlotGeometry(out Vector3 mid, out Vector3 outward, out Quaternion r);
        dir = outward;
        pos = mid + (outward * Constants.Television.SlotOutsideOffset);
        rot = r;
    }

    private void Press(string state)
    {
        _pressedAt[state] = Time.time;
        PlayState(state);
    }

    private void Echo(string state)
    {
        if (_pressedAt.TryGetValue(state, out float at) && Time.time - at < Constants.Television.LocalPressHold) return;
        PlayState(state);
    }

    private void PlayState(string state, float normalizedTime = 0f)
    {
        int layer = FindLayer(state);
        if (layer < 0) return;
        _animator!.Play(state, layer, normalizedTime);
        if (normalizedTime <= 0f) PlaySound(state);
    }

    private int FindLayer(string state)
    {
        if (_animator == null || _animator.runtimeAnimatorController == null) return -1;
        int hash = Animator.StringToHash(state);
        for (int i = 0; i < _animator.layerCount; i++)
        {
            if (_animator.HasState(i, hash)) return i;
        }
        return -1;
    }

    private bool HasParameter(string name)
    {
        if (_animator == null || _animator.runtimeAnimatorController == null) return false;
        foreach (AnimatorControllerParameter parameter in _animator.parameters)
        {
            if (parameter.name == name) return true;
        }
        return false;
    }

    private static CassetteItem? LocalHeldCassette()
    {
        PlayerControllerB? local = GameNetworkManager.Instance.localPlayerController;
        return local != null ? local.currentlyHeldObjectServer as CassetteItem : null;
    }

    private void OnDestroy()
    {
        DestroyVisual();
        _display?.Dispose();
        _display = null;
        if (_lampMaterial != null) Destroy(_lampMaterial);
    }
}
