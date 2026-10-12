using UnityEngine;

/// <summary>
/// Slides a material's texture over time: drifting waves on the sea, clouds on the wind.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class ScrollTexture : MonoBehaviour
{
    [Tooltip("Texture repeats per second along each axis.")]
    public Vector2 speed = new Vector2(0.01f, 0.004f);

    static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

    Material material;
    Vector2 startOffset;

    void Awake()
    {
        // .material makes a private copy, so the shared asset on disk is never modified.
        material = GetComponent<Renderer>().material;
        startOffset = material.GetTextureOffset(BaseMap);
    }

    void Update()
    {
        Vector2 offset = startOffset + speed * Time.time;
        material.SetTextureOffset(BaseMap, new Vector2(Mathf.Repeat(offset.x, 1f), Mathf.Repeat(offset.y, 1f)));
    }

    void OnDestroy()
    {
        if (material != null) Destroy(material);
    }
}
