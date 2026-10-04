using System.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace LipstickAR
{
    /// <summary>
    /// Dancing character that can put on lipstick: plays a looping dance clip, and on
    /// <see cref="ApplyLipstick"/> blends to a calm pose, IK-reaches the mouth with the
    /// lipstick and recolours the lips.
    /// </summary>
    public class LipstickDancer : MonoBehaviour
    {
        [Header("Rig")]
        public Animator animator;
        public AnimationClip danceClip;
        [Tooltip("Time in the dance clip used as the calm pose while applying lipstick.")]
        public float calmPoseTime = 0f;
        public Transform upperArm, foreArm, hand, head;

        [Header("Face (head-bone local space, measured in the editor)")]
        public Vector3 mouthLocal;
        public Vector3 faceForwardLocal = Vector3.forward;
        public Vector3 faceUpLocal = Vector3.up;
        public Vector3 leftEyeLocal, rightEyeLocal;
        [Tooltip("Head radius in character units (scale 1).")]
        public float mouthWidth = 0.05f;

        [Header("Props")]
        [Tooltip("Lipstick holder: origin at the lipstick base, +Y towards the tip, 1 unit long.")]
        public Transform lipstick;
        public float lipstickLength = 0.12f;
        public Renderer lipstickBullet;
        public int lipstickBulletMaterialIndex;
        public Mesh sphereMesh;
        public Material lipMaterial, eyeMaterial, hairMaterial;
        public Color startLipColor = new Color(0.85f, 0.6f, 0.55f);
        [Tooltip("Add cartoon eyes and hair (for faceless mannequins). Off for characters with a real face.")]
        public bool addEyesAndHair = true;
        [Tooltip("Hide the lip overlay until the first application (for characters with real lips).")]
        public bool hideLipsUntilApplied;

        public bool IsApplying { get; private set; }

        PlayableGraph m_Graph;
        AnimationMixerPlayable m_Mixer;
        AnimationClipPlayable m_Dance;
        float m_PoseWeight, m_IkWeight;
        Vector3 m_LipTarget;
        Material m_Lip, m_Bullet;
        GameObject[] m_LipParts;

        void Awake()
        {
            animator.applyRootMotion = false;
            m_Graph = PlayableGraph.Create("LipstickDancer");
            m_Graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(m_Graph, "Animation", animator);
            m_Mixer = AnimationMixerPlayable.Create(m_Graph, 2);
            var dance = m_Dance = AnimationClipPlayable.Create(m_Graph, danceClip);
            var pose = AnimationClipPlayable.Create(m_Graph, danceClip);
            pose.SetTime(calmPoseTime);
            pose.SetSpeed(0);
            m_Graph.Connect(dance, 0, m_Mixer, 0);
            m_Graph.Connect(pose, 0, m_Mixer, 1);
            output.SetSourcePlayable(m_Mixer);
            SetPoseWeight(0);
            m_Graph.Play();

            BuildFace();
            if (lipstickBullet != null)
            {
                var mats = lipstickBullet.materials;
                m_Bullet = mats[lipstickBulletMaterialIndex];
                m_Bullet.SetColor("_BaseColor", startLipColor);
            }
            lipstick.localScale = Vector3.one * lipstickLength;
        }

        void OnDestroy()
        {
            if (m_Graph.IsValid())
                m_Graph.Destroy();
        }

        void Update()
        {
            // Imported clips are not always flagged as looping, so wrap the time ourselves.
            if (m_Dance.IsValid() && m_Dance.GetTime() > danceClip.length)
                m_Dance.SetTime(m_Dance.GetTime() % danceClip.length);
        }

        void SetPoseWeight(float w)
        {
            m_PoseWeight = w;
            m_Mixer.SetInputWeight(0, 1 - w);
            m_Mixer.SetInputWeight(1, w);
        }

        Transform AddFacePart(string partName, Vector3 localPos, Vector3 worldSize, Material mat)
        {
            var go = new GameObject(partName, typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = sphereMesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var t = go.transform;
            t.SetParent(head, false);
            t.localPosition = localPos;
            t.localRotation = Quaternion.LookRotation(faceForwardLocal, faceUpLocal);
            // worldSize is in character units at scale 1; compensate for bone scale.
            var boneScale = head.lossyScale.x / transform.lossyScale.x;
            t.localScale = worldSize / boneScale;
            return t;
        }

        void BuildFace()
        {
            var unitsPerMeter = transform.lossyScale.x / head.lossyScale.x; // head-local units per character metre
            var up = faceUpLocal.normalized * unitsPerMeter;
            var fwd = faceForwardLocal.normalized * unitsPerMeter;

            m_Lip = new Material(lipMaterial);
            m_Lip.SetColor("_BaseColor", startLipColor);
            m_LipParts = new[]
            {
                AddFacePart("UpperLip", mouthLocal + up * 0.005f, new Vector3(mouthWidth, 0.01f, 0.014f), m_Lip).gameObject,
                AddFacePart("LowerLip", mouthLocal - up * 0.006f, new Vector3(mouthWidth * 0.85f, 0.012f, 0.014f), m_Lip).gameObject,
            };
            foreach (var part in m_LipParts)
                part.SetActive(!hideLipsUntilApplied);
            if (!addEyesAndHair)
                return;
            AddFacePart("LeftEye", leftEyeLocal, new Vector3(0.022f, 0.028f, 0.012f), eyeMaterial);
            AddFacePart("RightEye", rightEyeLocal, new Vector3(0.022f, 0.028f, 0.012f), eyeMaterial);

            // Hair: a cap over the back of the head plus a bun on top.
            var eyesMid = (leftEyeLocal + rightEyeLocal) * 0.5f;
            var headCenter = eyesMid - fwd * 0.075f + up * 0.01f;
            AddFacePart("HairCap", headCenter - fwd * 0.035f + up * 0.03f, new Vector3(0.2f, 0.2f, 0.19f), hairMaterial);
            AddFacePart("HairBun", headCenter - fwd * 0.07f + up * 0.11f, new Vector3(0.09f, 0.09f, 0.09f), hairMaterial);
        }

        /// <summary>Plays the "put on lipstick" sequence with the given colour.</summary>
        public void ApplyLipstick(Color color)
        {
            if (IsApplying)
                return;
            StartCoroutine(ApplyRoutine(color));
        }

        IEnumerator ApplyRoutine(Color color)
        {
            IsApplying = true;
            if (m_Bullet != null)
                m_Bullet.SetColor("_BaseColor", color);

            // Blend to the calm pose and bring the hand up.
            for (float t = 0; t < 1; t += Time.deltaTime / 0.6f)
            {
                SetPoseWeight(Mathf.SmoothStep(0, 1, t));
                m_IkWeight = Mathf.SmoothStep(0, 1, t);
                m_LipTarget = new Vector3(-0.5f, 0, 0);
                yield return null;
            }
            SetPoseWeight(1);
            m_IkWeight = 1;

            // Swipe across the lips a few times while the colour fades in.
            var startColor = m_Lip.GetColor("_BaseColor");
            foreach (var part in m_LipParts)
                part.SetActive(true);
            const float swipeTime = 2.4f;
            for (float t = 0; t < swipeTime; t += Time.deltaTime)
            {
                var phase = t / swipeTime;
                m_LipTarget = new Vector3(-0.5f * Mathf.Cos(phase * Mathf.PI * 6), phase < 0.5f ? 1 : -1, 0);
                m_Lip.SetColor("_BaseColor", Color.Lerp(startColor, color, phase));
                yield return null;
            }
            m_Lip.SetColor("_BaseColor", color);

            // Back to dancing.
            for (float t = 0; t < 1; t += Time.deltaTime / 0.6f)
            {
                SetPoseWeight(1 - Mathf.SmoothStep(0, 1, t));
                m_IkWeight = 1 - Mathf.SmoothStep(0, 1, t);
                yield return null;
            }
            SetPoseWeight(0);
            m_IkWeight = 0;
            IsApplying = false;
        }

        void LateUpdate()
        {
            var s = transform.lossyScale.x;
            var faceFwd = head.TransformDirection(faceForwardLocal).normalized;
            var faceUp = head.TransformDirection(faceUpLocal).normalized;
            var faceRight = Vector3.Cross(faceUp, faceFwd).normalized;
            var mouth = head.TransformPoint(mouthLocal);

            // Where the lipstick sits when it is touching the lips.
            var tip = mouth + faceFwd * (0.012f * s)
                      + faceRight * (m_LipTarget.x * mouthWidth * s)
                      + faceUp * (m_LipTarget.y * 0.006f * s);
            var axis = (-faceFwd * 0.8f - faceRight * 0.6f).normalized; // tip points into the face, lipstick held from the right
            var applyBase = tip - axis * (lipstickLength * s);

            if (m_IkWeight > 0.001f)
            {
                var gripTarget = applyBase - axis * (0.02f * s);
                SolveTwoBoneIK(gripTarget, m_IkWeight);
            }

            // Lipstick: held upright in the hand while dancing, pointed at the lips while applying.
            var handUp = (hand.position - foreArm.position).normalized;
            var holdBase = hand.position + handUp * (0.05f * s);
            var holdRot = Quaternion.LookRotation(transform.forward, transform.up);
            var applyRot = Quaternion.FromToRotation(Vector3.up, axis);
            lipstick.SetPositionAndRotation(
                Vector3.Lerp(holdBase, applyBase, m_IkWeight),
                Quaternion.Slerp(holdRot, applyRot, m_IkWeight));
        }

        void SolveTwoBoneIK(Vector3 target, float weight)
        {
            var aLocal0 = upperArm.localRotation;
            var bLocal0 = foreArm.localRotation;

            Vector3 a = upperArm.position, b = foreArm.position, c = hand.position;
            const float eps = 0.0001f;
            var lab = (b - a).magnitude;
            var lcb = (c - b).magnitude;
            var lat = Mathf.Clamp((target - a).magnitude, eps, lab + lcb - eps);

            var acab0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((c - a).normalized, (b - a).normalized), -1, 1));
            var babc0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((a - b).normalized, (c - b).normalized), -1, 1));
            var acab1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2 * lab * lat), -1, 1));
            var babc1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2 * lab * lcb), -1, 1));

            // Bend in the plane of the current arm, falling back to "elbow down".
            var axis0 = Vector3.Cross(c - a, b - a);
            if (axis0.sqrMagnitude < 1e-8f)
                axis0 = Vector3.Cross(c - a, -transform.up);
            axis0.Normalize();

            upperArm.rotation = Quaternion.AngleAxis((acab1 - acab0) * Mathf.Rad2Deg, axis0) * upperArm.rotation;
            foreArm.rotation = Quaternion.AngleAxis((babc1 - babc0) * Mathf.Rad2Deg, axis0) * foreArm.rotation;
            upperArm.rotation = Quaternion.FromToRotation(hand.position - a, target - a) * upperArm.rotation;

            upperArm.localRotation = Quaternion.Slerp(aLocal0, upperArm.localRotation, weight);
            foreArm.localRotation = Quaternion.Slerp(bLocal0, foreArm.localRotation, weight);
        }
    }
}
