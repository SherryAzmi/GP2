using UnityEngine;

/// <summary>
/// Bends the patient's virtual shin in the beach scene to match the measured knee angle,
/// so the player sees their own leg move. The shin's pivot must be at the knee.
/// </summary>
public class VirtualLegPose : MonoBehaviour
{
    public JointAngleCalculator angleSource;
    [Tooltip("Virtual shin to rotate (pivot at the knee)")]
    public Transform shin;

    [Tooltip("Shin local X rotation when the leg is straight (0 degrees)")]
    public float straightLocalX = 342f;
    [Tooltip("Point at the heel, in the shin's local space")]
    public Vector3 heelLocalPoint;
    [Tooltip("Knee angle used when there is no angle source (e.g. the non-operated leg resting)")]
    public float fixedAngle = 45f;

    Quaternion straightRotation;

    void Awake()
    {
        if (!shin) shin = transform;
        Vector3 e = shin.localEulerAngles;
        straightRotation = Quaternion.Euler(straightLocalX, e.y, e.z);
    }

    void LateUpdate()
    {
        shin.localRotation = RotationFor(angleSource ? angleSource.CurrentKneeAngle : fixedAngle);
    }

    Quaternion RotationFor(float kneeAngle) => straightRotation * Quaternion.Euler(-kneeAngle, 0f, 0f);

    /// <summary>Where the heel would be (world space) if the knee were bent to this angle.</summary>
    public Vector3 HeelPositionAt(float kneeAngle)
    {
        Vector3 local = shin.localPosition + RotationFor(kneeAngle) * Vector3.Scale(heelLocalPoint, shin.localScale);
        return shin.parent ? shin.parent.TransformPoint(local) : local;
    }

    public Vector3 HeelPosition => shin.TransformPoint(heelLocalPoint);

    // ---------------- Guide ("shadow") leg ----------------

    Transform ghost, marker;
    MaterialPropertyBlock ghostBlock;
    static readonly int ColorId = Shader.PropertyToID("_Color"), RimId = Shader.PropertyToID("_RimColor");
    static readonly Color Aim = new Color(0.3f, 0.9f, 1f), Match = new Color(0.35f, 1f, 0.45f);

    /// <summary>
    /// Shows a dark see-through copy of the shin bent to <paramref name="kneeAngle"/>, and a glowing ring on the sand
    /// under where the heel should go, so the patient can just match the shadow instead of reading numbers.
    /// <paramref name="matched"/> turns the guide green.
    /// </summary>
    public void ShowGhost(float kneeAngle, bool matched, Material ghostMaterial, float groundY)
    {
        if (!ghostMaterial) return;
        if (!ghost) BuildGhost(ghostMaterial);
        ghost.gameObject.SetActive(true);
        marker.gameObject.SetActive(true);
        ghost.localPosition = shin.localPosition;
        ghost.localRotation = RotationFor(kneeAngle);

        Vector3 heel = HeelPositionAt(kneeAngle);
        marker.position = new Vector3(heel.x, groundY + 0.004f, heel.z);
        float pulse = 1f + 0.12f * Mathf.Sin(Time.time * 4f);
        marker.localScale = new Vector3(0.16f * pulse, 0.002f, 0.16f * pulse);

        Color rim = matched ? Match : Aim;
        ghostBlock.SetColor(RimId, rim);
        ghostBlock.SetColor(ColorId, new Color(0.01f, 0.02f, 0.05f, matched ? 0.4f : 0.72f));
        ghost.GetComponent<Renderer>().SetPropertyBlock(ghostBlock);
        ghostBlock.SetColor(ColorId, new Color(rim.r, rim.g, rim.b, 0.45f));
        marker.GetComponent<Renderer>().SetPropertyBlock(ghostBlock);
    }

    public void HideGhost()
    {
        if (ghost) ghost.gameObject.SetActive(false);
        if (marker) marker.gameObject.SetActive(false);
    }

    void BuildGhost(Material material)
    {
        ghostBlock = new MaterialPropertyBlock();
        var go = new GameObject(shin.name + "_Guide", typeof(MeshFilter), typeof(MeshRenderer));
        ghost = go.transform;
        ghost.SetParent(shin.parent, false);
        ghost.localScale = shin.localScale * 1.04f; // a touch bigger, so the real leg sits inside it when matched
        go.GetComponent<MeshFilter>().sharedMesh = shin.GetComponent<MeshFilter>().sharedMesh;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(disc.GetComponent<Collider>());
        disc.name = shin.name + "_HeelTarget";
        marker = disc.transform;
        var mr = disc.GetComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
