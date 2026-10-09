using UnityEngine;
namespace DriftSkate
{
    [RequireComponent(typeof(TextMesh))]
    public class ShopText : MonoBehaviour
    {
        Material _material;
        void OnEnable()
        {
            var renderer = GetComponent<MeshRenderer>();
            _material = new Material(renderer.sharedMaterial);
            renderer.sharedMaterial = _material;
            Font.textureRebuilt += Rebuilt;
            Rebuilt(GetComponent<TextMesh>().font);
        }
        void Rebuilt(Font font)
        {
            if (_material != null && font != null && font == GetComponent<TextMesh>().font)
                _material.mainTexture = font.material.mainTexture;
        }
        void OnDisable() { Font.textureRebuilt -= Rebuilt; if (_material != null) Destroy(_material); }
    }
}
