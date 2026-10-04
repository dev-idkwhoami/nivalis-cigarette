using Nivalis;
using Nivalis.Playables;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace NivalisMods.Cigarette;

internal sealed class CannabisEffects : IDisposable
{
    private readonly HighSession _session = new();
    private readonly List<Apparition> _apparitions = new();
    private AnimatedSequence[]? _sequences;
    private HighCameraEffect? _cameraEffect;
    private PlayerCharacter? _character;
    private Vector3 _lastPosition;
    private float _spawnIn, _movingFor, _retryAssetsIn, _cameraStrength;
    private int _scene = -1;
    private bool _failed, _reportedAssets;
    private bool _reportedCredit;
    private string? _lastSuppression;
    private int _missedPlacements;

    internal void BeginSmoke()
    {
        if (_scene != SceneManager.GetActiveScene().handle) Dispose();
        _scene = SceneManager.GetActiveScene().handle;
        _character = PlayerManager._instance?.LocalPlayer?.Character;
        if (_character != null) _lastPosition = _character.transform.position;
        _session.BeginSmoke();
        _spawnIn = UnityEngine.Random.Range(3f, 6f);
        _failed = false;
        _reportedCredit = false;
        Plugin.Logger.LogInfo("Cannabis session started; consumed draws will earn high time.");
    }

    internal void Credit(float burn, float duration)
    {
        if (_scene == SceneManager.GetActiveScene().handle && !_failed)
        {
            _session.Credit(burn, duration, Plugin.HighDurationMultiplier.Value);
            if (!_reportedCredit && _session.Remaining > 0f)
            {
                Plugin.Logger.LogInfo($"Cannabis first draw credited: {_session.Remaining:F1}s remaining.");
                _reportedCredit = true;
            }
        }
    }

    internal void SuspendCamera()
    {
        _cameraStrength = 0f;
        if (_cameraEffect != null) _cameraEffect.Strength = 0f;
    }

    internal void Tick(bool gameplay, bool smoking, bool holdingSmoke)
    {
        try
        {
            var character = PlayerManager._instance?.LocalPlayer?.Character;
            if (_scene != SceneManager.GetActiveScene().handle || character == null || character != _character)
            { Dispose(); return; }
            if (_failed || _session.Remaining <= 0f) { ClearVisuals(); return; }
            var delta = gameplay ? Time.deltaTime : 0f;
            _session.Tick(delta, smoking);
            var strength = _session.Strength;
            var camera = character.Controller.Camera;
            // Camera focus flags are not gameplay visibility: ordinary camera targeting
            // can set them, and zoom can lock movement. Use explicit UI/boat/sleep state.
            var visible = !holdingSmoke && gameplay && camera != null && camera.isActiveAndEnabled &&
                character.DrivenBoat == null &&
                !character.Controller.HandsAnimator.sleeping;
            var suppression = holdingSmoke ? "smoking/lowering" : !gameplay ? "UI/pause/focus" : camera == null || !camera.isActiveAndEnabled ? "camera inactive"
                : character.DrivenBoat != null ? "boat" : character.Controller.HandsAnimator.sleeping ? "sleep" : "none";
            if (suppression != _lastSuppression)
            {
                Plugin.Logger.LogInfo($"Cannabis visibility: {suppression}; high {_session.Remaining:F1}s; smoking={holdingSmoke}.");
                _lastSuppression = suppression;
            }
            if (camera != null && (_cameraEffect == null || _cameraEffect.gameObject != camera.gameObject))
            {
                if (_cameraEffect != null) { _cameraEffect.Release(); Object.Destroy(_cameraEffect); }
                _cameraEffect = camera.gameObject.AddComponent<HighCameraEffect>();
            }
            // Keep the entire smoking/lowering animation clear, including a second smoke
            // during an existing high. Ease the camera effect in only after putting it down.
            _cameraStrength = !visible || holdingSmoke ? 0f : Mathf.MoveTowards(_cameraStrength, strength, delta / 2f);
            if (_cameraEffect != null) _cameraEffect.Strength = _cameraStrength;
            for (var i = _apparitions.Count - 1; i >= 0; i--)
                if (!_apparitions[i].Tick(delta, strength, visible, camera != null ? camera.transform.position : Vector3.zero))
                { _apparitions[i].Dispose(); _apparitions.RemoveAt(i); }
            var position = character.transform.position;
            if (delta > 0f && Vector3.ProjectOnPlane(position - _lastPosition, Vector3.up).magnitude / delta > 0.3f)
                _movingFor = 1f;
            else _movingFor = Mathf.Max(0f, _movingFor - delta);
            _lastPosition = position;
            if (!visible) return;
            _spawnIn -= delta;
            _retryAssetsIn -= delta;
            if (_spawnIn > 0f) return;
            _spawnIn = UnityEngine.Random.Range(6f, 12f);
            if (_apparitions.Count >= 2 || strength < 0.1f) return;
            if (_sequences == null && _retryAssetsIn <= 0f)
            {
                // One scan on demand, then at most once every 30 seconds if assets are unavailable.
                _retryAssetsIn = 30f;
                var found = Resources.FindObjectsOfTypeAll<AnimatedSequence>()
                    .Where(x => x != null && x.name == "Female_Singer" &&
                        x.keyframes != null && x.keyframes.Length > 0 && x.keyframes[0]?.texture != null).ToArray();
                if (found.Length > 0) _sequences = found;
                if (!_reportedAssets || found.Length > 0)
                    Plugin.Logger.LogInfo("Cannabis apparition sequences: " + (found.Length == 0 ? "not loaded here; will retry" : string.Join(", ", found.Select(x => x.name))));
                _reportedAssets = true;
            }
            var available = _sequences?.Where(x => x != null).ToArray();
            if (available == null || available.Length == 0) { _sequences = null; return; }
            var sequence = available[UnityEngine.Random.Range(0, available.Length)];
            var height = Finite(Plugin.ApparitionHeight.Value, 2.1f, 0.5f, 3.75f);
            var texture = sequence.keyframes[0].texture;
            var width = height * texture.width / texture.height;
            if (TryPlace(character, camera!, height, width, out var point, out var rotation, out var placementReason))
            {
                var layer = 0;
                for (var i = 0; i < 32; i++) if ((camera!.cullingMask & (1 << i)) != 0) { layer = i; break; }
                _apparitions.Add(new Apparition(sequence, point, rotation, height, layer));
                _missedPlacements = 0;
                Plugin.Logger.LogInfo($"Cannabis singer spawned: {Vector3.Distance(point, camera!.transform.position):F1}m away, {point.y - character.transform.position.y:F1}m vertical offset.");
            }
            else if (_missedPlacements++ % 5 == 0)
                Plugin.Logger.LogInfo("Cannabis placement: " + placementReason + "; will retry.");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning("Cannabis effects stopped: " + e);
            Dispose();
            _failed = true;
        }
    }

    private bool TryPlace(PlayerCharacter character, Camera camera, float height, float width, out Vector3 point, out Quaternion rotation, out string reason)
    {
        point = default; rotation = Quaternion.identity; reason = "placed";
        var groundFailures = 0; var clearanceFailures = 0; var sightFailures = 0; var supportFailures = 0; var nearbyFailures = 0;
        var eye = camera.transform;
        var forward = Vector3.ProjectOnPlane(eye.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.5f) forward = Vector3.ProjectOnPlane(character.transform.forward, Vector3.up).normalized;
        var mask = character.Controller.HandsAnimator.environmentLayer;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var moving = _movingFor > 0f;
            var direction = Quaternion.Euler(0f, UnityEngine.Random.Range(moving ? -35f : -110f, moving ? 35f : 110f), 0f) * forward;
            var distance = moving ? UnityEngine.Random.Range(9f, 18f) : UnityEngine.Random.Range(3f, 7f);
            var candidate = character.transform.position + direction * distance;
            // Include streets/terraces below balconies, not only the player's current floor.
            if (!Physics.Raycast(candidate + Vector3.up * 2f, Vector3.down, out var hit, 122f, mask, QueryTriggerInteraction.Ignore) ||
                hit.normal.y < 0.9f || hit.collider == null || hit.collider.attachedRigidbody != null)
            { groundFailures++; continue; }
            point = hit.point + Vector3.up * 0.08f;
            rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(point - eye.position, Vector3.up).normalized);
            var placedPoint = point;
            if (_apparitions.Any(x => Vector3.Distance(x.Position, placedPoint) < 2.5f)) { nearbyFailures++; continue; }
            var center = point + Vector3.up * (height * 0.5f);
            // Reserve the whole turning footprint so camera-facing rotation cannot clip a wall.
            if (Physics.CheckBox(center, new Vector3(width * 0.5f + 0.1f, height * 0.5f, width * 0.5f + 0.1f), rotation, mask, QueryTriggerInteraction.Ignore))
            { clearanceFailures++; continue; }
            var sight = center - eye.position;
            if (Physics.Raycast(eye.position, sight.normalized, sight.magnitude, mask, QueryTriggerInteraction.Ignore))
            { sightFailures++; continue; }
            // Support the turning footprint; don't bridge curbs, gaps or ledges.
            var supported = true;
            foreach (var corner in new[] { new Vector3(-1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, -1), new Vector3(1, 0, 1) })
            {
                var foot = point + rotation * corner * (width * 0.5f);
                if (!Physics.Raycast(foot + Vector3.up * 0.2f, Vector3.down, out var support, 0.45f, mask, QueryTriggerInteraction.Ignore) || support.normal.y < 0.9f)
                { supported = false; break; }
            }
            if (supported) return true;
            supportFailures++;
        }
        reason = $"12 attempts rejected: ground={groundFailures}, clearance={clearanceFailures}, line-of-sight={sightFailures}, support={supportFailures}, nearby={nearbyFailures}";
        return false;
    }

    internal static float Finite(float value, float fallback, float min, float max) => float.IsFinite(value) ? Mathf.Clamp(value, min, max) : fallback;

    private void ClearVisuals()
    {
        foreach (var apparition in _apparitions) apparition.Dispose();
        _apparitions.Clear();
        if (_cameraEffect != null) { _cameraEffect.Release(); Object.Destroy(_cameraEffect); }
        _cameraEffect = null;
        _cameraStrength = 0f;
    }

    public void Dispose()
    {
        ClearVisuals();
        _session.Clear(); _sequences = null; _character = null; _scene = -1;
        _movingFor = 0f; _retryAssetsIn = 0f; _reportedAssets = false;
        _lastSuppression = null; _missedPlacements = 0;
    }
}
