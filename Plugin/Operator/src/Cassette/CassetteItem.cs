using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEngine;

namespace Operator.Cassette;

public sealed class CassetteItem : GrabbableObject
{
    public string CassetteId { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public string Timestamp { get; private set; } = string.Empty;
    public float Duration { get; private set; }
    public string BreakdownRaw { get; private set; } = string.Empty;
    public bool IsEncoding { get; private set; }

    private ulong _encodingOwner;
    private static readonly HashSet<CassetteItem> _encoding = new();

    internal static bool AnyEncoding
    {
        get
        {
            _encoding.RemoveWhere(tape => tape == null);
            return _encoding.Count > 0;
        }
    }

    internal static bool IsEncodingFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        foreach (CassetteItem tape in _encoding)
        {
            if (tape == null) continue;
            if (string.Equals(tape.FileName, fileName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    internal static CassetteItem? FindById(string id, string fileName)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(fileName)) return null;
        foreach (CassetteItem tape in UnityEngine.Object.FindObjectsOfType<CassetteItem>())
        {
            if (tape == null) continue;
            if (!string.Equals(tape.CassetteId, id, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(tape.FileName, fileName, StringComparison.OrdinalIgnoreCase)) return tape;
        }
        return null;
    }

    internal static CassetteItem? FindByFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return null;
        foreach (CassetteItem tape in UnityEngine.Object.FindObjectsOfType<CassetteItem>())
        {
            if (tape == null) continue;
            if (string.Equals(tape.FileName, fileName, StringComparison.OrdinalIgnoreCase)) return tape;
        }
        return null;
    }

    private Coroutine? _scanRefreshRoutine;
    private bool? _wasScannable;
    private bool _setupDone;

    private Rigidbody? _tossBody;
    private Dawn.Utils.ClientNetworkTransform? _netTransform;
    private Coroutine? _settleRoutine;
    private bool _motionComponentsCached;
    private bool _physicsDriven;
    private Vector3 _tossOriginPosition;

    private bool _arcActive;
    private bool _arcLanded;
    private bool _arcPinning;
    private float _arcTime;
    private float _arcAir;
    private float _arcUp;
    private float _arcGravity;
    private float _arcHop;
    private float _arcTip;
    private float _arcLandY;
    private Vector3 _arcTipAxis;
    private Vector3 _arcStart;
    private Vector3 _arcEnd;
    private Quaternion _arcStartRot;
    private Quaternion _arcEndRot;
    private Quaternion _arcTurn;

    internal int SaveHandle;
    private CassetteSaveData? _pendingLoadedData;

    private void Awake()
    {
        SetupItem();
    }

    private void SetupItem()
    {
        if (_setupDone) return;
        _setupDone = true;

        if (itemProperties == null && CassetteRegistrar.CassetteItemSO != null)
        {
            itemProperties = CassetteRegistrar.CassetteItemSO;
        }

        grabbable = true;
        grabbableToEnemies = true;
        deactivated = false;
        isHeld = false;
        isPocketed = false;
        heldByPlayerOnServer = false;

        int propsLayer = LayerMask.NameToLayer("Props");
        if (propsLayer >= 0 && gameObject.layer != propsLayer)
        {
            gameObject.layer = propsLayer;
        }

        if (propColliders == null || propColliders.Length == 0)
        {
            var all = GetComponentsInChildren<Collider>(includeInactive: false);
            var kept = new List<Collider>(all.Length);
            foreach (var c in all)
            {
                if (c == null) continue;
                if (c.GetComponent<ScanNodeProperties>() != null) continue;
                kept.Add(c);
            }
            propColliders = kept.ToArray();
        }

        if (mainObjectRenderer == null)
        {
            mainObjectRenderer = GetComponentInChildren<MeshRenderer>(includeInactive: false);
        }

        foreach (var r in GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            if (r == null) continue;
            if (!r.enabled) r.enabled = true;
            if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
        }
    }

    public void ServerSetMetadata(string cassetteId, string fileName, string timestamp, float durationSec, int totalValue, string breakdown, bool encoding)
    {
        if (!IsServer) return;
        try
        {
            scrapValue = CassetteLimits.ClampStoredValue(totalValue);
            string id = (cassetteId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(id))
            {
                id = CassetteIdGenerator.GenerateId(fileName ?? Guid.NewGuid().ToString());
            }
            PushMetadataClientRpc(
                id.ToLowerInvariant(),
                fileName ?? string.Empty,
                timestamp ?? string.Empty,
                Mathf.Max(0f, durationSec),
                scrapValue,
                breakdown ?? string.Empty,
                encoding);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to set metadata for cassette '{cassetteId}' with recording '{fileName}': {ex}");
        }
    }

    public override int GetItemDataToSave()
    {
        if (string.IsNullOrEmpty(CassetteId)) return 0;
        if (!CassettePersistence.IsAvailable) return 0;
        if (SaveHandle == 0) SaveHandle = CassettePersistence.AllocateHandle();
        CassettePersistence.Store(SaveHandle, new CassetteSaveData
        {
            Id = CassetteId,
            FileName = FileName,
            Timestamp = Timestamp,
            Duration = Duration,
            Value = scrapValue,
            Breakdown = BreakdownRaw,
        });
        return SaveHandle;
    }

    public override void LoadItemSaveData(int saveData)
    {
        if (saveData <= 0) return;
        SaveHandle = saveData;
        if (CassettePersistence.IsAvailable && CassettePersistence.TryLoad(saveData, out CassetteSaveData data))
        {
            _pendingLoadedData = data;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        UpdateNetTransform();
        StartCoroutine(ReleaseTossBody());
        if (IsServer && !isHeld && !isPocketed && !_physicsDriven)
        {
            ApplyRestingRotation();
        }
        if (IsServer && _pendingLoadedData != null)
        {
            CassetteSaveData d = _pendingLoadedData;
            _pendingLoadedData = null;
            ServerSetMetadata(d.Id, d.FileName, d.Timestamp, d.Duration, d.Value, d.Breakdown, encoding: false);
        }
        if (!IsServer)
        {
            RequestMetadataServerRpc();
        }
    }

    private void ApplyRestingRotation()
    {
        if (itemProperties == null) return;
        Vector3 rest = itemProperties.restingRotation;
        Quaternion resting = Quaternion.Euler(rest.x, transform.eulerAngles.y, rest.z);
        transform.rotation = resting;
        CacheMotionComponents();
        if (_tossBody != null) _tossBody.rotation = resting;
    }

    private void CacheMotionComponents()
    {
        if (_motionComponentsCached) return;
        _motionComponentsCached = true;
        _tossBody = GetComponent<Rigidbody>();
        _netTransform = GetComponent<Dawn.Utils.ClientNetworkTransform>();
    }

    private void DestroyTossBody()
    {
        CacheMotionComponents();
        if (_tossBody != null)
        {
            Destroy(_tossBody);
            _tossBody = null;
        }
    }

    private IEnumerator ReleaseTossBody()
    {
        yield return new WaitForSeconds(0.25f);
        while (_physicsDriven || _settleRoutine != null || _arcActive)
        {
            yield return null;
        }
        DestroyTossBody();
    }

    private void UpdateNetTransform()
    {
        CacheMotionComponents();
        if (_netTransform == null) return;
        bool shouldSync = !isHeld && !isPocketed;
        if (_netTransform.enabled != shouldSync)
        {
            _netTransform.enabled = shouldSync;
        }
    }

    public bool ServerBeginEjectToss(Vector3 direction, float outForce, float upForce, float settleVelocity, float timeoutSeconds)
    {
        if (!IsServer) return false;
        CacheMotionComponents();
        if (_tossBody == null) return false;

        UpdateNetTransform();

        Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;

        _physicsDriven = true;
        _tossOriginPosition = transform.position;
        _tossBody.isKinematic = false;
        _tossBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _tossBody.interpolation = RigidbodyInterpolation.Interpolate;
        _tossBody.useGravity = true;
        _tossBody.velocity = Vector3.zero;
        _tossBody.angularVelocity = Vector3.zero;
        _tossBody.AddForce((dir * outForce) + (Vector3.up * upForce), ForceMode.Impulse);
        _tossBody.AddTorque(UnityEngine.Random.insideUnitSphere * outForce, ForceMode.Impulse);

        if (_settleRoutine != null) StopCoroutine(_settleRoutine);
        _settleRoutine = StartCoroutine(SettleAfterToss(settleVelocity, timeoutSeconds));
        return true;
    }

    internal bool ServerStartArc(VideoCameraItem camera, Vector3 land, float yaw, float up)
    {
        if (!IsServer) return false;
        Transform? parent = transform.parent;
        Vector3 landLocal = parent != null ? parent.InverseTransformPoint(land) : land;
        StartArc(camera, landLocal, yaw, up);
        StartEjectArcClientRpc(camera.NetworkObject, landLocal, yaw, up);
        return true;
    }

    [ClientRpc]
    private void StartEjectArcClientRpc(NetworkObjectReference camera, Vector3 landLocal, float yaw, float up)
    {
        if (IsServer) return;
        VideoCameraItem? item = camera.TryGet(out NetworkObject cameraObject)
            ? cameraObject.GetComponent<VideoCameraItem>()
            : null;
        StartArc(item, landLocal, yaw, up);
    }

    private void StartArc(VideoCameraItem? camera, Vector3 landLocal, float yaw, float up)
    {
        if (isHeld || parentObject != null) return;

        _arcStart = transform.position;
        _arcStartRot = transform.rotation;
        if (camera != null && camera.TryGetEjectPose(out Vector3 posePosition, out Quaternion poseRotation))
        {
            _arcStart = posePosition;
            _arcStartRot = poseRotation;
            camera.HideEjectDecor();
        }

        _arcEnd = landLocal;
        Vector3 land = GetLandPoint();
        _arcLandY = land.y;
        Vector3 rest = itemProperties != null ? itemProperties.restingRotation : new Vector3(90f, 0f, 90f);
        _arcEndRot = Quaternion.Euler(rest.x, yaw, rest.z);

        _arcTipAxis = _arcStartRot * Vector3.right;
        Vector3 target = Vector3.ProjectOnPlane(_arcEndRot * Vector3.forward, _arcTipAxis);
        _arcTip = target.sqrMagnitude > 0.0001f
            ? Vector3.SignedAngle(_arcStartRot * Vector3.forward, target, _arcTipAxis)
            : 0f;
        Quaternion tipped = Quaternion.AngleAxis(_arcTip, _arcTipAxis) * _arcStartRot;
        _arcTurn = _arcEndRot * Quaternion.Inverse(tipped);

        _arcGravity = Mathf.Max(0.01f, -Physics.gravity.y);
        float climb = land.y - _arcStart.y + Constants.Camera.EjectClearance;
        if (climb > 0f) up = Mathf.Max(up, Mathf.Sqrt(2f * _arcGravity * climb));
        _arcUp = up;
        float fall = Mathf.Max(0f, (up * up / (2f * _arcGravity)) + _arcStart.y - land.y);
        _arcAir = (up / _arcGravity) + Mathf.Sqrt(2f * fall / _arcGravity);
        _arcHop = Constants.Camera.EjectHop * Mathf.Clamp01(fall);
        _arcTime = 0f;
        _arcLanded = false;
        _arcPinning = false;
        _arcActive = true;

        CacheMotionComponents();
        if (_tossBody != null) _tossBody.interpolation = RigidbodyInterpolation.None;
        transform.SetPositionAndRotation(_arcStart, _arcStartRot);
    }

    private Vector3 GetLandPoint()
    {
        Transform? parent = transform.parent;
        return parent != null ? parent.TransformPoint(_arcEnd) : _arcEnd;
    }

    private void UpdateArc()
    {
        if (!_arcActive && !_arcPinning) return;
        if (isHeld || parentObject != null)
        {
            _arcActive = false;
            _arcPinning = false;
            return;
        }
        if (_arcPinning)
        {
            UpdatePin();
            return;
        }

        _arcTime += Time.deltaTime;
        float t = _arcTime;
        Vector3 land = GetLandPoint();
        if (t < _arcAir)
        {
            float u = t / _arcAir;
            float e = Mathf.SmoothStep(0f, 1f, u);
            Vector3 pos = Vector3.Lerp(_arcStart, land, u);
            pos.y = _arcStart.y + (_arcUp * t) - (0.5f * _arcGravity * t * t) + ((land.y - _arcLandY) * u);
            Quaternion tip = Quaternion.AngleAxis(_arcTip * e, _arcTipAxis) * _arcStartRot;
            transform.SetPositionAndRotation(pos, Quaternion.Slerp(Quaternion.identity, _arcTurn, e) * tip);
            return;
        }

        if (!_arcLanded)
        {
            _arcLanded = true;
            if (itemProperties != null) PlayDropSFX();
        }

        float after = t - _arcAir;
        float hopTime = Constants.Camera.EjectHopTime;
        float rockTime = Constants.Camera.EjectRockTime;
        Quaternion endRot = _arcEndRot;
        if (after < hopTime)
        {
            land.y += _arcHop * Mathf.Sin(Mathf.PI * after / hopTime);
        }
        else if (after < hopTime + rockTime)
        {
            float r = (after - hopTime) / rockTime;
            float angle = Constants.Camera.EjectRock * (1f - r) * Mathf.Sin(r * Mathf.PI * 3f);
            endRot = _arcEndRot * Quaternion.AngleAxis(angle, Vector3.right);
        }
        else
        {
            FinishArc();
            return;
        }
        transform.SetPositionAndRotation(land, endRot);
    }

    private void FinishArc()
    {
        _arcActive = false;
        transform.SetPositionAndRotation(GetLandPoint(), _arcEndRot);
        Vector3 landed = transform.localPosition;
        fallTime = 1f;
        hasHitGround = true;
        reachedFloorTarget = true;
        startFallingPosition = landed;
        targetFloorPosition = landed;
        if (!IsServer)
        {
            _arcPinning = true;
            _arcTime = 0f;
        }
        RefreshScanNodes();
    }

    private void UpdatePin()
    {
        _arcTime += Time.deltaTime;
        Vector3 land = GetLandPoint();
        float gap = Constants.Camera.EjectPinDistance;
        bool close = (transform.position - land).sqrMagnitude < gap * gap
                     && Quaternion.Angle(transform.rotation, _arcEndRot) < Constants.Camera.EjectPinAngle;
        if (close || _arcTime >= Constants.Camera.EjectPinTimeout)
        {
            _arcPinning = false;
            return;
        }
        transform.SetPositionAndRotation(land, _arcEndRot);
    }

    private IEnumerator SettleAfterToss(float settleVelocity, float timeoutSeconds)
    {
        float elapsed = 0f;
        float verticalOffset = itemProperties != null ? itemProperties.verticalOffset : 0f;
        bool clampToFloor = false;
        Vector3 floorRest = transform.position;
        yield return new WaitForFixedUpdate();

        while (elapsed < timeoutSeconds)
        {
            if (_tossBody == null) { _settleRoutine = null; yield break; }

            Vector3 here = transform.position;
            float originY = Mathf.Max(here.y, _tossOriginPosition.y) + 1f;
            if (Physics.Raycast(new Vector3(here.x, originY, here.z), Vector3.down, out RaycastHit floorHit,
                    (originY - here.y) + 80f, Constants.Cassette.FloorRaycastMask, QueryTriggerInteraction.Ignore))
            {
                float restY = floorHit.point.y + verticalOffset + Constants.Cassette.EjectRestHeightOffset;
                if (here.y <= restY)
                {
                    floorRest = new Vector3(here.x, restY, here.z);
                    clampToFloor = true;
                    break;
                }
            }

            if (_tossBody.IsSleeping()) break;
            if (elapsed > 0.4f
                && _tossBody.velocity.magnitude < settleVelocity
                && _tossBody.angularVelocity.magnitude < settleVelocity) break;
            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        _settleRoutine = null;

        if (!clampToFloor)
        {
            Vector3 here = transform.position;
            float originY = Mathf.Max(here.y, _tossOriginPosition.y) + 1f;
            if (Physics.Raycast(new Vector3(here.x, originY, here.z), Vector3.down, out RaycastHit floorHit,
                    (originY - here.y) + 80f, Constants.Cassette.FloorRaycastMask, QueryTriggerInteraction.Ignore)
                && here.y < floorHit.point.y)
            {
                floorRest = new Vector3(here.x, floorHit.point.y + verticalOffset + Constants.Cassette.EjectRestHeightOffset, here.z);
                clampToFloor = true;
            }
        }

        if (_tossBody != null)
        {
            _tossBody.velocity = Vector3.zero;
            _tossBody.angularVelocity = Vector3.zero;
            _tossBody.collisionDetectionMode = CollisionDetectionMode.Discrete;
            _tossBody.isKinematic = true;
            _tossBody.useGravity = false;
            _tossBody.interpolation = RigidbodyInterpolation.None;
        }

        if (clampToFloor)
        {
            transform.position = floorRest;
        }

        if (itemProperties != null)
        {
            Vector3 rest = itemProperties.restingRotation;
            Quaternion resting = Quaternion.Euler(rest.x, transform.eulerAngles.y, rest.z);
            transform.rotation = resting;
            if (_tossBody != null) _tossBody.rotation = resting;
        }

        Vector3 landed = transform.position;
        Vector3 localLanded = transform.parent != null ? transform.parent.InverseTransformPoint(landed) : landed;
        fallTime = 1f;
        hasHitGround = true;
        reachedFloorTarget = true;
        startFallingPosition = localLanded;
        targetFloorPosition = localLanded;
        _physicsDriven = false;

        RefreshScanNodes();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestMetadataServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;
        if (string.IsNullOrEmpty(CassetteId)) return;

        ulong requester = rpcParams.Receive.SenderClientId;
        var sendTo = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { requester } },
        };
        PushMetadataClientRpc(
            CassetteId, FileName, Timestamp, Duration, scrapValue, BreakdownRaw, IsEncoding, sendTo);
    }

    [ClientRpc]
    private void PushMetadataClientRpc(string cassetteId, string fileName, string timestamp, float duration, int value, string breakdown, bool encoding, ClientRpcParams rpcParams = default)
    {
        CassetteId = cassetteId ?? string.Empty;
        FileName = fileName ?? string.Empty;
        Timestamp = timestamp ?? string.Empty;
        Duration = duration;
        BreakdownRaw = breakdown ?? string.Empty;
        scrapValue = Mathf.Max(0, value);
        IsEncoding = encoding;
        if (encoding) _encoding.Add(this);
        else _encoding.Remove(this);
        UpdateScrapTooltip();
        RefreshScanNodes();
        CassetteShellColor.Apply(transform, CassetteId, scrapValue);
        CassetteHudIcon.Refresh(this);

        TerminalCassettePlayback.RegisterCassetteId(CassetteId);
    }

    public void ServerWatchEncode(ulong owner)
    {
        if (!IsServer) return;
        _encodingOwner = owner;
        StartCoroutine(WaitForEncode());
    }

    private IEnumerator WaitForEncode()
    {
        var wait = new WaitForSeconds(1f);
        float deadline = Time.realtimeSinceStartup + Constants.Cassette.EncodeTimeout;

        while (IsEncoding)
        {
            yield return wait;
            if (!IsEncoding) yield break;

            bool ownerGone = NetworkManager == null || !IsOwnerConnected();
            bool late = Time.realtimeSinceStartup >= deadline;
            if (!ownerGone && !late) continue;

            if (late)
            {
                Plugin.Log.LogWarning($"Stopped waiting for tape '{CassetteId.ToUpperInvariant()}' to finish writing after {Constants.Cassette.EncodeTimeout:0}s.");
            }
            ServerSetMetadata(CassetteId, FileName, Timestamp, Duration, scrapValue, BreakdownRaw, encoding: false);
            yield break;
        }
    }

    private bool IsOwnerConnected()
    {
        NetworkManager? manager = NetworkManager;
        if (manager == null) return false;
        foreach (ulong id in manager.ConnectedClientsIds)
        {
            if (id == _encodingOwner) return true;
        }
        return false;
    }

    internal bool IsEncodingBy(ulong sender) => IsServer && IsEncoding && sender == _encodingOwner;

    public bool ServerFinishEncode(ulong sender, bool ok, float duration)
    {
        if (!IsEncodingBy(sender)) return false;

        if (ok)
        {
            ServerSetMetadata(CassetteId, FileName, Timestamp, CassetteLimits.ClampDuration(duration, sender), scrapValue, BreakdownRaw, encoding: false);
            return true;
        }

        if (NetworkManager != null && sender != NetworkManager.LocalClientId)
        {
            Plugin.Log.LogWarning($"Tape '{CassetteId.ToUpperInvariant()}' came out blank, its recording could not be written.");
        }
        ServerSetMetadata(CassetteId, string.Empty, Timestamp, 0f, 0, string.Empty, encoding: false);
        return true;
    }

    public override void OnNetworkDespawn()
    {
        _encoding.Remove(this);
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        _encoding.Remove(this);
        base.OnDestroy();
    }

    public override void Update()
    {
        CacheMotionComponents();
        if (!isHeld && parentObject == null
            && (_arcActive || (_netTransform != null && _netTransform.enabled && (!IsOwner || _physicsDriven))))
        {
            if (currentUseCooldown >= 0f) currentUseCooldown -= Time.deltaTime;
            return;
        }
        base.Update();
    }

    public override void LateUpdate()
    {
        base.LateUpdate();
        UpdateArc();
    }

    public override void Start()
    {
        base.Start();
        SetupAudio();
        UpdateScrapTooltip();
        RefreshScanNodes();
        if (_scanRefreshRoutine == null)
        {
            _scanRefreshRoutine = StartCoroutine(ScanRefreshLoop());
        }
    }

    private void SetupAudio()
    {
        var source = GetComponent<AudioSource>();
        if (source == null) return;

        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = Constants.Cassette.MinSoundDistance;
        source.maxDistance = Constants.Cassette.MaxSoundDistance;
    }

    public override void EquipItem()
    {
        base.EquipItem();
        UpdateNetTransform();
        UpdateScrapTooltip();
        RefreshScanNodes();
    }

    public override void PocketItem()
    {
        base.PocketItem();
        UpdateNetTransform();
        RefreshScanNodes();
    }

    public override void DiscardItem()
    {
        base.DiscardItem();
        UpdateNetTransform();
        RefreshScanNodes();
    }

    private IEnumerator ScanRefreshLoop()
    {
        while (true)
        {
            RefreshScanNodes();
            yield return new WaitForSeconds(0.5f);
        }
    }

    private void UpdateScrapTooltip()
    {
        if (itemProperties == null) return;
        string idShown = string.IsNullOrEmpty(CassetteId) ? "?" : CassetteId.ToUpperInvariant();
        itemProperties.toolTips = new[]
        {
            $"ID: {idShown}  •  ${scrapValue}",
            $"Terminal: '{Constants.Terminal.KeywordPlay} {idShown}'",
        };

        if (playerHeldBy != null
            && playerHeldBy == StartOfRound.Instance.localPlayerController)
        {
            HUDManager.Instance.ChangeControlTipMultiple(itemProperties.toolTips, holdingItem: true, itemProperties);
        }
    }

    private void RefreshScanNodes()
    {
        var scanNodes = GetComponentsInChildren<ScanNodeProperties>(includeInactive: true);
        if (scanNodes == null || scanNodes.Length == 0) return;

        bool canBeScannedNow = !isHeld && !isPocketed && !isHeldByEnemy && playerHeldBy == null;
        if (_wasScannable != canBeScannedNow)
        {
            _wasScannable = canBeScannedNow;
        }
        int scanLayer = LayerMask.NameToLayer("ScanNode");
        if (scanLayer < 0) scanLayer = 22;
        string idShown = string.IsNullOrWhiteSpace(CassetteId) ? "?" : CassetteId.ToUpperInvariant();
        string subText = BuildScanSubText(idShown, Mathf.Max(0, scrapValue));

        for (int i = 0; i < scanNodes.Length; i++)
        {
            var scan = scanNodes[i];
            if (scan == null) continue;

            if (!canBeScannedNow)
            {
                if (scan.transform == transform)
                {
                    scan.enabled = false;
                    Collider? hiddenCol = scan.GetComponent<Collider>();
                    if (hiddenCol != null) hiddenCol.enabled = false;
                }
                else if (scan.gameObject.activeSelf)
                {
                    scan.gameObject.SetActive(false);
                }
                continue;
            }

            ActivateScanHierarchy(scan.transform, transform);
            if (scanLayer >= 0) scan.gameObject.layer = scanLayer;

            scan.enabled = true;
            Collider? col = scan.GetComponent<Collider>();
            if (col == null)
            {
                var box = scan.gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(0.28f, 0.28f, 0.28f);
                col = box;
            }
            col.enabled = true;
            col.isTrigger = true;
            if (col is BoxCollider boxCol && boxCol.size.sqrMagnitude < 0.01f)
            {
                boxCol.size = new Vector3(0.28f, 0.28f, 0.28f);
            }

            if (string.IsNullOrWhiteSpace(scan.headerText)) scan.headerText = "Cassette";
            scan.scrapValue = Mathf.Max(0, scrapValue);
            scan.subText = subText;
            scan.nodeType = 2;
            scan.requiresLineOfSight = true;
            scan.minRange = Constants.Cassette.MinScanRange;
            scan.maxRange = Constants.Cassette.MaxScanRange;
        }
    }

    private static void ActivateScanHierarchy(Transform leaf, Transform root)
    {
        Transform? current = leaf;
        while (current != null)
        {
            if (!current.gameObject.activeSelf)
            {
                current.gameObject.SetActive(true);
            }
            if (current == root) break;
            current = current.parent;
        }
    }

    private static string BuildScanSubText(string idShown, int value)
    {
        return $"ID: {idShown} | Value: ${Mathf.Max(0, value)}";
    }

    public string? FindLocalPath()
    {
        if (string.IsNullOrWhiteSpace(FileName)) return null;

        string target = CassetteLibrary.GetMergedName(FileName);
        foreach (string path in CassetteLibrary.GetCandidatePaths(target))
        {
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
