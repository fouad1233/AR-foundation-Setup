using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace LipstickAR
{
    /// <summary>
    /// Tracks a lipstick in the camera image by its colour: finds the largest lipstick-coloured
    /// blob on screen, estimates its distance from its size, guesses which lipstick it is
    /// (nearest reference colour) and keeps a mini character standing on top of it, putting
    /// that lipstick on. Buttons are a fallback for bad lighting.
    /// </summary>
    public class LipstickScanner : MonoBehaviour
    {
        [Serializable]
        public class Lipstick
        {
            public string name;
            [Tooltip("Colour of the product as the camera sees it.")]
            public Color referenceColor;
            [Tooltip("Colour put on the character's lips.")]
            public Color lipColor;
        }

        public ARCameraManager cameraManager;
        public ARRaycastManager raycastManager;
        public Camera arCamera;
        public LipstickDancer characterPrefab;
        public float characterScale = 0.3f;
        public float spawnDistance = 0.9f;

        public Lipstick[] lipsticks =
        {
            new Lipstick { name = "Dior", referenceColor = new Color32(174, 106, 109, 255), lipColor = new Color32(214, 86, 92, 255) },
            new Lipstick { name = "YSL", referenceColor = new Color32(231, 157, 171, 255), lipColor = new Color32(236, 92, 140, 255) },
        };

        [Header("Tracking")]
        [Tooltip("Scale of the character while she stands on the lipstick (1 = life size).")]
        public float trackedScale = 0.1f;
        [Tooltip("Approximate length of the coloured part of the lipstick, in metres.")]
        public float lipstickRealLength = 0.09f;
        [Tooltip("Smallest blob, as a fraction of the sampled image, that counts as a lipstick.")]
        public float minBlobFraction = 0.003f;
        public float sampleInterval = 0.06f;
        public int framesToConfirm = 5;
        public float cooldown = 4f;

        const int k_Width = 72, k_Height = 128;

        LipstickDancer m_Character;
        ARCameraBackground m_Background;
        RenderTexture m_Rt;
        Texture2D m_Readback;
        bool[] m_Mask;
        int[] m_Label, m_Queue;

        float m_NextSample, m_CooldownUntil, m_LastSeen = -10;
        int m_Streak, m_StreakIndex = -1, m_AppliedIndex = -1;
        Rect m_Blob;               // viewport space, y up
        int m_BlobIndex = -1;
        Vector3 m_Target;
        string m_Status = "Show me a lipstick";
        Texture2D m_White;
        static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();

        bool Tracking => Time.time - m_LastSeen < 0.5f;

        void Awake()
        {
            m_White = Texture2D.whiteTexture;
        }

        void OnDestroy()
        {
            if (m_Rt != null)
                m_Rt.Release();
        }

        void Update()
        {
            if (Time.time >= m_NextSample)
            {
                m_NextSample = Time.time + sampleInterval;
                Detect();
            }

            if (m_Character == null || !Tracking)
                return;

            // Follow the lipstick smoothly and face the camera.
            var t = m_Character.transform;
            t.position = Vector3.Lerp(t.position, m_Target, 1 - Mathf.Exp(-12 * Time.deltaTime));
            var toCam = Vector3.ProjectOnPlane(arCamera.transform.position - t.position, Vector3.up);
            if (toCam.sqrMagnitude > 1e-6f)
                t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(toCam), 1 - Mathf.Exp(-8 * Time.deltaTime));
        }

        void Detect()
        {
            if (m_Background == null)
                m_Background = arCamera.GetComponent<ARCameraBackground>();
            if (m_Background == null || m_Background.material == null)
                return;

            if (m_Rt == null)
            {
                m_Rt = new RenderTexture(k_Width, k_Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                m_Readback = new Texture2D(k_Width, k_Height, TextureFormat.RGBA32, false);
                m_Mask = new bool[k_Width * k_Height];
                m_Label = new int[k_Width * k_Height];
                m_Queue = new int[k_Width * k_Height];
            }

            // The background material applies the display transform, so this is the camera image
            // exactly as it appears on screen (rotated and cropped).
            Graphics.Blit(null, m_Rt, m_Background.material);
            var previous = RenderTexture.active;
            RenderTexture.active = m_Rt;
            m_Readback.ReadPixels(new Rect(0, 0, k_Width, k_Height), 0, 0, false);
            RenderTexture.active = previous;
            var pixels = m_Readback.GetPixels32();

            for (int i = 0; i < pixels.Length; i++)
            {
                Color.RGBToHSV(pixels[i], out var h, out var s, out var v);
                // Pink / red / coral, excluding most skin (orange-ish hue) and grey.
                m_Mask[i] = (h > 0.88f || h < 0.025f) && s > 0.2f && v > 0.25f;
                m_Label[i] = 0;
            }

            // Largest 4-connected blob.
            int bestCount = 0, bestLabel = 0, label = 0;
            for (int start = 0; start < m_Mask.Length; start++)
            {
                if (!m_Mask[start] || m_Label[start] != 0)
                    continue;
                label++;
                int head = 0, tail = 0, count = 0;
                m_Queue[tail++] = start;
                m_Label[start] = label;
                while (head < tail)
                {
                    var p = m_Queue[head++];
                    count++;
                    int x = p % k_Width, y = p / k_Width;
                    if (x > 0) Visit(p - 1, label, ref tail);
                    if (x < k_Width - 1) Visit(p + 1, label, ref tail);
                    if (y > 0) Visit(p - k_Width, label, ref tail);
                    if (y < k_Height - 1) Visit(p + k_Width, label, ref tail);
                }
                if (count > bestCount)
                {
                    bestCount = count;
                    bestLabel = label;
                }
            }

            int index = -1;
            if (bestCount >= minBlobFraction * m_Mask.Length)
            {
                int minX = k_Width, maxX = 0, minY = k_Height, maxY = 0;
                float r = 0, g = 0, b = 0;
                for (int i = 0; i < m_Label.Length; i++)
                {
                    if (m_Label[i] != bestLabel)
                        continue;
                    int x = i % k_Width, y = i / k_Width;
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                    r += pixels[i].r; g += pixels[i].g; b += pixels[i].b;
                }
                var avg = new Color(r / bestCount / 255f, g / bestCount / 255f, b / bestCount / 255f);
                index = Classify(avg);
                m_Blob = Rect.MinMaxRect((float)minX / k_Width, (float)minY / k_Height, (float)(maxX + 1) / k_Width, (float)(maxY + 1) / k_Height);
                m_LastSeen = Time.time;
                UpdateTarget();
            }

            if (index >= 0 && index == m_StreakIndex)
                m_Streak++;
            else
            {
                m_StreakIndex = index;
                m_Streak = index >= 0 ? 1 : 0;
            }
            if (index >= 0)
                m_BlobIndex = index;

            if (m_Character != null && m_Character.IsApplying)
                return;

            if (!Tracking)
            {
                if (Time.time > m_CooldownUntil)
                    m_Status = "Show me a lipstick";
                return;
            }
            if (index < 0)
                return;

            m_Status = lipsticks[index].name + "!";
            var changed = index != m_AppliedIndex || Time.time > m_CooldownUntil + 6f;
            if (m_Streak >= framesToConfirm && changed && Time.time > m_CooldownUntil)
                Trigger(index);
        }

        void Visit(int p, int label, ref int tail)
        {
            if (!m_Mask[p] || m_Label[p] != 0)
                return;
            m_Label[p] = label;
            m_Queue[tail++] = p;
        }

        void UpdateTarget()
        {
            // Distance from the apparent size of the lipstick (its longer side on screen).
            var aspect = (float)Screen.width / Screen.height;
            var extent = Mathf.Max(m_Blob.height, m_Blob.width * aspect);
            var viewHeight = 2f * Mathf.Tan(arCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var depth = Mathf.Clamp(lipstickRealLength / Mathf.Max(extent * viewHeight, 1e-3f), 0.12f, 1.5f);
            // She stands on top of the lipstick.
            m_Target = arCamera.ViewportToWorldPoint(new Vector3(m_Blob.center.x, m_Blob.yMax, depth));
            if (m_Character == null)
                return;
            m_Character.transform.localScale = Vector3.one * trackedScale;
        }

        int Classify(Color c)
        {
            Color.RGBToHSV(c, out var h, out var s, out var v);
            int best = -1;
            var bestDist = float.MaxValue;
            for (int i = 0; i < lipsticks.Length; i++)
            {
                Color.RGBToHSV(lipsticks[i].referenceColor, out var rh, out var rs, out var rv);
                var dh = Mathf.Abs(h - rh);
                dh = Mathf.Min(dh, 1 - dh) * 4;
                var d = dh * dh + (s - rs) * (s - rs) + (v - rv) * (v - rv);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }
            return best;
        }

        public void Trigger(int index)
        {
            var lipstick = lipsticks[index];
            m_CooldownUntil = Time.time + cooldown;
            m_Streak = 0;
            m_AppliedIndex = index;
            if (m_Character == null)
            {
                if (Tracking)
                {
                    m_Character = Instantiate(characterPrefab, m_Target, Quaternion.identity);
                    m_Character.transform.localScale = Vector3.one * trackedScale;
                }
                else
                    PlaceCharacter();
            }
            m_Character.ApplyLipstick(lipstick.lipColor);
            m_Status = "Applying " + lipstick.name + "!";
        }

        public void PlaceCharacter()
        {
            var cam = arCamera.transform;
            var flatForward = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 1e-4f)
                flatForward = Vector3.ProjectOnPlane(cam.up, Vector3.up);
            flatForward.Normalize();

            var distance = spawnDistance * 0.5f;
            var position = cam.position + flatForward * distance + Vector3.down * (trackedScale * 1.2f);
            if (raycastManager != null &&
                raycastManager.Raycast(new Vector2(Screen.width * 0.5f, Screen.height * 0.35f), s_Hits, UnityEngine.XR.ARSubsystems.TrackableType.PlaneWithinPolygon) &&
                s_Hits[0].distance < 2f)
                position = s_Hits[0].pose.position;

            var rotation = Quaternion.LookRotation(-flatForward, Vector3.up);
            if (m_Character == null)
                m_Character = Instantiate(characterPrefab, position, rotation);
            else
                m_Character.transform.SetPositionAndRotation(position, rotation);
            m_Character.transform.localScale = Vector3.one * trackedScale;
        }

        void OnGUI()
        {
            var scale = Screen.height / 1280f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            var w = Screen.width / scale;
            var h = Screen.height / scale;

            var label = new GUIStyle(GUI.skin.label) { fontSize = 34, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            GUI.color = new Color(0, 0, 0, 0.45f);
            GUI.DrawTexture(new Rect(0, 40, w, 70), m_White);
            GUI.color = Color.white;
            GUI.Label(new Rect(0, 40, w, 70), m_Status, label);

            // Box around the tracked lipstick.
            if (Tracking)
            {
                var rect = new Rect(m_Blob.xMin * w, (1 - m_Blob.yMax) * h, m_Blob.width * w, m_Blob.height * h);
                DrawFrame(rect, Color.white, 4);
                if (m_BlobIndex >= 0)
                {
                    var small = new GUIStyle(label) { fontSize = 28 };
                    GUI.Label(new Rect(rect.center.x - 150, rect.yMax + 4, 300, 40), lipsticks[m_BlobIndex].name, small);
                }
            }

            var button = new GUIStyle(GUI.skin.button) { fontSize = 32 };
            var bw = (w - 80) / 3;
            for (int i = 0; i < lipsticks.Length && i < 2; i++)
                if (GUI.Button(new Rect(20 + i * (bw + 20), h - 140, bw, 110), lipsticks[i].name, button) &&
                    (m_Character == null || !m_Character.IsApplying))
                    Trigger(i);
            if (GUI.Button(new Rect(20 + 2 * (bw + 20), h - 140, bw, 110), "Move here", button))
                PlaceCharacter();
        }

        void DrawFrame(Rect r, Color color, float thickness)
        {
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, thickness), m_White);
            GUI.DrawTexture(new Rect(r.x, r.yMax - thickness, r.width, thickness), m_White);
            GUI.DrawTexture(new Rect(r.x, r.y, thickness, r.height), m_White);
            GUI.DrawTexture(new Rect(r.xMax - thickness, r.y, thickness, r.height), m_White);
            GUI.color = Color.white;
        }
    }
}
