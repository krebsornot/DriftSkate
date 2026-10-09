using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>Bereich mit Bonus fuer Drifts (wie Clipping-Zonen in CarX). Wird als Box im Level platziert.</summary>
    public class DriftZone : MonoBehaviour
    {
        public static readonly List<DriftZone> All = new List<DriftZone>();

        public float multiplier = 1.5f;
        public Vector3 size = new Vector3(10, 4, 10);

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public bool Contains(Vector3 worldPos)
        {
            Vector3 local = transform.InverseTransformPoint(worldPos);
            return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.z) <= size.z * 0.5f && local.y > -1f && local.y < size.y;
        }

        public static DriftZone Find(Vector3 worldPos)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i].Contains(worldPos)) return All[i];
            return null;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.35f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(new Vector3(0, size.y * 0.5f, 0), size);
        }
    }
}
