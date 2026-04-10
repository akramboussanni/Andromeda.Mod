using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Windwalk.Net;
using Andromeda.Mod.Settings;
using LobbySettings = Andromeda.Mod.Settings.AndromedaSettings;
using ClientSettings = Andromeda.Mod.Settings.AndromedaClientSettings;

namespace Andromeda.Mod.Patches
{
    [HarmonyPatch]
    public static class FirstPersonCameraPatch
    {
        private static readonly FieldInfo CameraField = AccessTools.Field(typeof(CameraController), "cam");
        private static readonly FieldInfo YawField = AccessTools.Field(typeof(CameraController), "yaw");
        private static readonly MethodInfo SetProbeMethod = AccessTools.Method(typeof(CameraController), "SetProbe", new[] { typeof(Vector3), typeof(Quaternion?) });
        private static readonly FieldInfo CursorPositionField = AccessTools.Field(typeof(LockableCursorInputModule), "position");
        private static readonly FieldInfo DirectionalCursorImageField = AccessTools.Field(typeof(DirectionalCursor), "cursor");
        private static readonly FieldInfo DirectionalCursorWorldField = AccessTools.Field(typeof(DirectionalCursor), "worldSpaceCursor");

        private const float EyeHeight = 1.55f;
        private const float MaxPitchAngle = 32f;
        private const float PitchSensitivityScale = 0.75f;

        private static float _pitch;
        private static GameObject _hiddenLocalPlayer;
        private static readonly HashSet<GameObject> _hiddenVisualObjects = new HashSet<GameObject>();

        private static float ReadMouseYawInput()
        {
            float raw = Input.GetAxisRaw("Mouse x");
            if (Mathf.Abs(raw) < 0.0001f)
                raw = Input.GetAxisRaw("Mouse X");
            return raw;
        }

        private static float ReadMousePitchInput()
        {
            float raw = Input.GetAxisRaw("Mouse y");
            if (Mathf.Abs(raw) < 0.0001f)
                raw = Input.GetAxisRaw("Mouse Y");
            return raw;
        }

        private static bool ShouldUseFirstPersonMouse()
        {
            if (!LobbySettings.FirstPersonEnabled.Value) return false;
            if (GameInput.IsBlockedDead) return false;
            if (GameInput.IsBlockedMenu || GameInput.IsBlockedChat || GameInput.IsBlockedConsole) return false;
            if (GameInput.IsBlockedCinematic) return false;
            return true;
        }

        private static void ApplyFirstPersonMouseLock()
        {
            if (!ShouldUseFirstPersonMouse()) return;

            try
            {
                var cursorModule = Singleton.Existing<LockableCursorInputModule>();
                if ((UnityEngine.Object)cursorModule != (UnityEngine.Object)null)
                {
                    if (!LockableCursorInputModule.Locked)
                        LockableCursorInputModule.Locked = true;

                    CursorPositionField?.SetValue(cursorModule, new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
                }

                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            catch { }
        }

        private static void HideLocalPlayerVisuals(GameObject localGo)
        {
            if ((UnityEngine.Object)localGo == (UnityEngine.Object)null)
                return;

            if ((UnityEngine.Object)_hiddenLocalPlayer != (UnityEngine.Object)localGo)
            {
                RestoreLocalPlayerVisuals();
                _hiddenLocalPlayer = localGo;
            }

            try
            {
                if (_hiddenVisualObjects.Count == 0)
                {
                    var playerModel = localGo.GetComponentInChildren<PlayerModelClient>(true);
                    if ((UnityEngine.Object)playerModel != (UnityEngine.Object)null)
                    {
                        var characterRenderer = playerModel.CharacterRenderer;
                        if ((UnityEngine.Object)characterRenderer != (UnityEngine.Object)null)
                        {
                            var go = characterRenderer.gameObject;
                            if ((UnityEngine.Object)go != (UnityEngine.Object)null && go.activeSelf)
                            {
                                go.SetActive(false);
                                _hiddenVisualObjects.Add(go);
                            }
                        }

                        var repairKit = playerModel.RepairKit;
                        if ((UnityEngine.Object)repairKit != (UnityEngine.Object)null && repairKit.activeSelf)
                        {
                            repairKit.SetActive(false);
                            _hiddenVisualObjects.Add(repairKit);
                        }

                        var itemPivots = playerModel.ItemPivots;
                        if (itemPivots != null)
                        {
                            foreach (var pivot in itemPivots)
                            {
                                var renderer = pivot?.renderer;
                                if ((UnityEngine.Object)renderer == (UnityEngine.Object)null) continue;
                                var go = renderer.gameObject;
                                if ((UnityEngine.Object)go == (UnityEngine.Object)null || !go.activeSelf) continue;

                                go.SetActive(false);
                                _hiddenVisualObjects.Add(go);
                            }
                        }
                    }
                }
                else
                {
                    // Re-apply in case gameplay scripts re-enable visual children.
                    foreach (var go in _hiddenVisualObjects)
                    {
                        if ((UnityEngine.Object)go == (UnityEngine.Object)null) continue;
                        if (go.activeSelf) go.SetActive(false);
                    }
                }
            }
            catch { }
        }

        private static void RestoreLocalPlayerVisuals()
        {
            foreach (var go in _hiddenVisualObjects)
            {
                if ((UnityEngine.Object)go == (UnityEngine.Object)null) continue;
                go.SetActive(true);
            }
            _hiddenVisualObjects.Clear();

            _hiddenLocalPlayer = null;
        }

        [HarmonyPatch(typeof(CameraController), "LateUpdate")]
        [HarmonyPrefix]
        public static bool PrefixLateUpdate(CameraController __instance)
        {
            if (!LobbySettings.FirstPersonEnabled.Value)
            {
                _pitch = 0f;
                RestoreLocalPlayerVisuals();
                FirstPersonHudOverlay.Restore();
                return true;
            }
            if (__instance == null) return true;
            if (GameInput.IsBlockedDead)
            {
                RestoreLocalPlayerVisuals();
                FirstPersonHudOverlay.Restore();
                return true;
            }
            if (__instance.CurrentMode == CameraController.Mode.Reorient)
                return true;

            try
            {
                var local = PlayerManagerClient.Instance.FetchLocal();
                if (!local.Item2)
                {
                    RestoreLocalPlayerVisuals();
                    return true;
                }

                var localGo = local.Item1.GetGameObject();
                if ((UnityEngine.Object)localGo == (UnityEngine.Object)null)
                {
                    RestoreLocalPlayerVisuals();
                    return true;
                }

                ApplyFirstPersonMouseLock();
                HideLocalPlayerVisuals(localGo);
                FirstPersonHudOverlay.UpdateLocalOverhead(localGo);

                float yaw = (float)(YawField?.GetValue(__instance) ?? 0f);
                yaw += ReadMouseYawInput() * ClientSettings.FirstPersonYawSensitivity.Value;
                YawField?.SetValue(__instance, yaw);

                _pitch -= ReadMousePitchInput() * ClientSettings.FirstPersonYawSensitivity.Value * PitchSensitivityScale;
                _pitch = Mathf.Clamp(_pitch, -MaxPitchAngle, MaxPitchAngle);

                var cam = CameraField?.GetValue(__instance) as Camera;
                if ((UnityEngine.Object)cam == (UnityEngine.Object)null) return true;

                var eyePos = localGo.transform.position + Vector3.up * EyeHeight;
                var lookRot = Quaternion.Euler(_pitch, yaw, 0f);
                var forward = lookRot * Vector3.forward;

                cam.transform.position = eyePos;
                cam.transform.forward = forward;

                SetProbeMethod?.Invoke(__instance, new object[] { eyePos, new Quaternion?(cam.transform.rotation) });
                return false;
            }
            catch
            {
                return true;
            }
        }

        [HarmonyPatch(typeof(CameraController), "UpdateLocked")]
        [HarmonyPrefix]
        public static bool PrefixUpdateLocked(CameraController __instance)
        {
            if (!LobbySettings.FirstPersonEnabled.Value) return true;
            if (__instance == null) return true;
            if (GameInput.IsBlockedDead) return true;

            try
            {
                var local = PlayerManagerClient.Instance.FetchLocal();
                if (!local.Item2) return true;

                var localGo = local.Item1.GetGameObject();
                if ((UnityEngine.Object)localGo == (UnityEngine.Object)null) return true;

                ApplyFirstPersonMouseLock();
                HideLocalPlayerVisuals(localGo);
                FirstPersonHudOverlay.UpdateLocalOverhead(localGo);

                float yaw = (float)(YawField?.GetValue(__instance) ?? 0f);
                yaw += ReadMouseYawInput() * ClientSettings.FirstPersonYawSensitivity.Value;
                YawField?.SetValue(__instance, yaw);

                _pitch -= ReadMousePitchInput() * ClientSettings.FirstPersonYawSensitivity.Value * PitchSensitivityScale;
                _pitch = Mathf.Clamp(_pitch, -MaxPitchAngle, MaxPitchAngle);

                var cam = CameraField?.GetValue(__instance) as Camera;
                if ((UnityEngine.Object)cam == (UnityEngine.Object)null) return true;

                var eyePos = localGo.transform.position + Vector3.up * EyeHeight;
                var lookRot = Quaternion.Euler(_pitch, yaw, 0f);
                var forward = lookRot * Vector3.forward;

                cam.transform.position = eyePos;
                cam.transform.forward = forward;

                SetProbeMethod?.Invoke(__instance, new object[] { eyePos, new Quaternion?(cam.transform.rotation) });
                return false;
            }
            catch
            {
                return true;
            }
        }

        [HarmonyPatch(typeof(CursorTargeterClient), "Setup")]
        [HarmonyPrefix]
        public static bool PrefixCursorTargeterSetup(CursorTargeterClient __instance)
        {
            if (!LobbySettings.FirstPersonEnabled.Value) return true;
            if (__instance == null) return true;

            try
            {
                var cam = Camera.main;
                if ((UnityEngine.Object)cam == (UnityEngine.Object)null) return true;

                var local = PlayerManagerClient.Instance.FetchLocal();
                if (!local.Item2) return true;

                var localGo = local.Item1.GetGameObject();
                if ((UnityEngine.Object)localGo == (UnityEngine.Object)null) return true;

                // Mouse Y only affects camera pitch, not gameplay aim.
                Vector3 planarForward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
                if (planarForward.sqrMagnitude < 0.0001f)
                    planarForward = Vector3.ProjectOnPlane(localGo.transform.forward, Vector3.up);
                if (planarForward.sqrMagnitude < 0.0001f)
                    planarForward = Vector3.forward;
                planarForward.Normalize();

                Vector3 end = cam.transform.position + planarForward * 8f;
                Ray ray = new Ray(cam.transform.position, planarForward);
                float enter;
                if (new Plane(Vector3.up, Vector3.up * 1.3f).Raycast(ray, out enter))
                    end = ray.GetPoint(enter);

                var settingsField = AccessTools.Field(typeof(CursorTargeterShared), "settings");
                var settings = settingsField?.GetValue(__instance) as CursorTargeterSettings;
                if (settings != null && settings.intersectsTerrain)
                {
                    RaycastHit hitInfo;
                    if (Physics.Linecast(localGo.transform.position, end, out hitInfo, (int)settings.layerMask))
                        end = hitInfo.point;
                }

                __instance.gameObject.transform.position = end;
                EntityManagerClient.Instance.SendReliable((Entity.Base)__instance, (BaseMessage)new CursorTargeterShared.SetPositionMsg() { position = end });
                return false;
            }
            catch
            {
                return true;
            }
        }

        [HarmonyPatch(typeof(DirectionalCursor), "LateUpdate")]
        [HarmonyPrefix]
        public static bool PrefixDirectionalCursorLateUpdate(DirectionalCursor __instance)
        {
            if (!LobbySettings.FirstPersonEnabled.Value) return true;
            if (__instance == null) return true;

            try
            {
                var cursorImage = DirectionalCursorImageField?.GetValue(__instance) as UnityEngine.UI.Image;
                if (cursorImage != null)
                    cursorImage.gameObject.SetActive(false);

                var worldCursor = DirectionalCursorWorldField?.GetValue(__instance) as GameObject;
                if (worldCursor != null)
                    worldCursor.SetActive(false);
            }
            catch { }

            return false;
        }
    }

    [HarmonyPatch]
    public static class FirstPersonPlayerControlsPatch
    {
        public static MethodBase TargetMethod()
        {
            var playerClientType = AccessTools.TypeByName("PlayerClient");
            if (playerClientType == null) return null;
            return AccessTools.Method(playerClientType, "ReadControls", new[] { typeof(PlayerSettings), typeof(GameInput.Frame), typeof(Vector3) });
        }

        [HarmonyPrefix]
        public static bool PrefixReadControls(PlayerSettings settings, GameInput.Frame input, Vector3 position, ref PlayerShared.Controls __result)
        {
            if (!LobbySettings.FirstPersonEnabled.Value)
                return true;

            var cam = Camera.main;
            if ((UnityEngine.Object)cam == (UnityEngine.Object)null)
                return true;

            Vector3 forwardPlanar = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (forwardPlanar.sqrMagnitude < 0.0001f)
                forwardPlanar = Vector3.forward;
            forwardPlanar.Normalize();

            Vector3 rightPlanar = Vector3.Cross(Vector3.up, forwardPlanar);
            if (rightPlanar.sqrMagnitude < 0.0001f)
                rightPlanar = Vector3.right;
            rightPlanar.Normalize();

            Vector3 moveWorld = rightPlanar * input.Move.x + forwardPlanar * input.Move.y;

            __result = new PlayerShared.Controls
            {
                move = new Vector2(moveWorld.x, moveWorld.z),
                look = new Vector2(forwardPlanar.x, forwardPlanar.z)
            };

            return false;
        }
    }

    [HarmonyPatch]
    public static class FirstPersonVisionPatch
    {
        [HarmonyPatch(typeof(VisionEffect), "Update")]
        [HarmonyPrefix]
        public static bool PrefixVisionEffectUpdate()
        {
            if (!LobbySettings.FirstPersonEnabled.Value)
                return true;

            var client = AndromedaClient.Instance;
            if ((UnityEngine.Object)client == (UnityEngine.Object)null)
                return true;

            if (client.Phase == AndromedaShared.RoundPhase.Crisis)
            {
                // Keep crisis generator-down vision behavior from vanilla.
                return true;
            }

            // Non-crisis FPS: unlimited world visibility, no occluder cutoffs.
            foreach (var instance in HideOutsideVision.Instances)
            {
                instance.SetVisibility(true);
            }

            return false;
        }
    }

    public static class FirstPersonHudOverlay
    {
        private static readonly Vector2 FpsHudOffset = new Vector2(-24f, 24f);
        private static readonly Vector3 FpsHudScale = new Vector3(1.2f, 1.2f, 1f);

        private static OverheadUI _localOverhead;
        private static KeepOverObject _keepOverObject;
        private static RectTransform _rect;
        private static Vector2 _anchorMin;
        private static Vector2 _anchorMax;
        private static Vector2 _pivot;
        private static Vector2 _anchoredPos;
        private static Vector3 _localScale;
        private static bool _keepOverWasEnabled;

        public static void UpdateLocalOverhead(GameObject localGo)
        {
            if ((UnityEngine.Object)localGo == (UnityEngine.Object)null)
                return;

            var overhead = localGo.GetComponentInChildren<OverheadUI>(true);
            if ((UnityEngine.Object)overhead == (UnityEngine.Object)null)
                return;

            if ((UnityEngine.Object)_localOverhead != (UnityEngine.Object)overhead)
            {
                Restore();

                _localOverhead = overhead;
                _rect = overhead.GetComponent<RectTransform>();
                _keepOverObject = overhead.GetComponent<KeepOverObject>();

                if (_rect != null)
                {
                    _anchorMin = _rect.anchorMin;
                    _anchorMax = _rect.anchorMax;
                    _pivot = _rect.pivot;
                    _anchoredPos = _rect.anchoredPosition;
                    _localScale = _rect.localScale;
                }

                if (_keepOverObject != null)
                {
                    _keepOverWasEnabled = _keepOverObject.enabled;
                }
            }

            if ((UnityEngine.Object)_localOverhead == (UnityEngine.Object)null)
                return;

            _localOverhead.gameObject.SetActive(true);

            if (_keepOverObject != null)
                _keepOverObject.enabled = false;

            if (_rect != null)
            {
                _rect.anchorMin = new Vector2(1f, 0f);
                _rect.anchorMax = new Vector2(1f, 0f);
                _rect.pivot = new Vector2(1f, 0f);
                _rect.anchoredPosition = FpsHudOffset;
                _rect.localScale = FpsHudScale;
            }
        }

        public static void Restore()
        {
            if ((UnityEngine.Object)_localOverhead == (UnityEngine.Object)null)
                return;

            if (_keepOverObject != null)
                _keepOverObject.enabled = _keepOverWasEnabled;

            if (_rect != null)
            {
                _rect.anchorMin = _anchorMin;
                _rect.anchorMax = _anchorMax;
                _rect.pivot = _pivot;
                _rect.anchoredPosition = _anchoredPos;
                _rect.localScale = _localScale;
            }

            _localOverhead = null;
            _keepOverObject = null;
            _rect = null;
        }
    }
}
