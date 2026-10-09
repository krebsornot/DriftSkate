using UnityEngine;

namespace DriftSkate
{
    /// <summary>Small additive greeting and gaze on top of the authored shop idle.</summary>
    public sealed class SprayShopKeeper : MonoBehaviour
    {
        Animator animator;
        Transform head;
        float greeting, yaw, pitch;
        void Start()
        {
            animator = GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman) head = animator.GetBoneTransform(HumanBodyBones.Head);
        }
        public void Greet() => greeting = 1.2f;
        void LateUpdate()
        {
            if (head == null) return;
            var player = PlayerAvatar.Local;
            float targetYaw = 0, targetPitch = 0;
            if (player != null && player.Mode == PlayerMode.Skating)
            {
                var delta = player.skater.transform.position + Vector3.up * 1.55f - head.position;
                var local = transform.InverseTransformDirection(delta);
                if (delta.sqrMagnitude < 25 && local.z > 0)
                {
                    targetYaw = Mathf.Clamp(Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,-24,24);
                    targetPitch = Mathf.Clamp(-Mathf.Atan2(local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg,-12,12);
                }
            }
            float blend = 1-Mathf.Exp(-4*Time.deltaTime);
            yaw = Mathf.Lerp(yaw,targetYaw,blend); pitch = Mathf.Lerp(pitch,targetPitch,blend);
            greeting = Mathf.Max(0,greeting-Time.deltaTime);
            float nod = greeting > 0 ? Mathf.Sin((1.2f-greeting)/1.2f*Mathf.PI*2)*5 : 0;
            head.rotation = Quaternion.AngleAxis(yaw,transform.up) * Quaternion.AngleAxis(pitch+nod,transform.right) * head.rotation;
        }
    }
}
