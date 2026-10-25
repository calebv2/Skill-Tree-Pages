using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RevampedSkills.Progression;

// Local presentation only; authoritative hand detection remains in ShrinePages.
internal sealed class ShrineAttachmentView
{
    private static GameObject? prefab;
    private static AssetBundle? bundle;
    private readonly GameObject root;
    private Material? glow, muted;
    private Transform? previousFace, nextFace;
    private Vector3 previousRest, nextRest;
    private bool wasTouching;
    private float nextPulse;
    internal Transform Previous { get; }
    internal Transform Next { get; }

    private static byte[] Resource(string name)
    {
        using var stream = typeof(ShrineAttachmentView).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing embedded shrine resource: " + name);
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
    private static Vector3 Vector(JToken t) => new((float)t["x"]!, (float)t["y"]!, (float)t["z"]!);

    internal static ShrineAttachmentView? Create(Transform visualizer)
    {
        var data = JObject.Parse(System.Text.Encoding.UTF8.GetString(Resource("RevampedSkills.ShrinePlacement")));
        Transform? parent = visualizer;
        while (parent != null && parent.name != (string)data["parentName"]! &&
            parent.name != "ruins_visualise_base") parent = parent.parent;
        if (parent == null) return null; // Other visualizers keep their existing text controls.
        return new ShrineAttachmentView(parent, data);
    }

    private ShrineAttachmentView(Transform parent, JObject data)
    {
        Material? stone = null;
        Color light = new(0.75f, 1.22f, 1.38f);
        if (!ApplicationManager.IsHeadless)
        {
            foreach (Renderer renderer in parent.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    if (material.name.StartsWith("Ruins_02_glow_m")) stone = material;
                    if (material.name.StartsWith("Ruins_shrine_handtouch_LOD0") && material.HasProperty("_EmissionColor"))
                        light = material.GetColor("_EmissionColor");
                }
        }
        GameObject? instance = null;
        if (!ApplicationManager.IsHeadless)
        {
            try
            {
                if (prefab == null)
                {
                    bundle = AssetBundle.LoadFromMemory(Resource("RevampedSkills.ShrineBundle"));
                    if (bundle == null) throw new InvalidOperationException("Unity could not load shrine bundle.");
                    prefab = bundle.LoadAsset<GameObject>("ShrineNavigationRuntime");
                }
                if (prefab == null || stone == null) throw new InvalidOperationException("Shrine prefab or live stone material missing.");
                instance = UnityEngine.Object.Instantiate(prefab);
            }
            catch (Exception error) { Core.Logger.Warning("Shrine attachment using text fallback: " + error.Message); }
        }
        root = instance ?? new GameObject("RevampedSkills Shrine Navigation");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = Vector(data["position"]!);
        root.transform.localScale = Vector(data["scale"]!);
        JToken q = data["rotation"]!;
        root.transform.localRotation = new Quaternion((float)q["x"]!, (float)q["y"]!, (float)q["z"]!, (float)q["w"]!);
        Previous = Anchor("PreviousTouch", Vector(data["previous"]!));
        Next = Anchor("NextTouch", Vector(data["next"]!));
        previousFace = root.transform.Find("PreviousButton/PressFace");
        nextFace = root.transform.Find("NextButton/PressFace");
        if (previousFace != null) previousRest = previousFace.localPosition;
        if (nextFace != null) nextRest = nextFace.localPosition;
        if (ApplicationManager.IsHeadless) return;
        Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (instance == null)
        {
            Label("Previous", Previous.localPosition, "<", font);
            Label("Next", Next.localPosition, ">", font);
            Label("OwnedCount", new Vector3(0, .03f, -.09f), "", font);
            Label("PageCount", new Vector3(0, -.03f, -.09f), "", font);
        }
        else
        {
            glow = new Material(Shader.Find("Unlit/Color") ?? stone!.shader);
            glow.color = light;
            muted = new Material(glow); muted.color = light * .18f;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                if (r.GetComponent<TextMesh>() == null)
                    r.sharedMaterial = r.name.StartsWith("ArrowStroke") || r.name == "Crystal" ? glow : stone;
        }
        foreach (TextMesh text in root.GetComponentsInChildren<TextMesh>(true))
        {
            text.font = font;
            text.GetComponent<Renderer>().sharedMaterial = font.material;
        }
    }
    private Transform Anchor(string name, Vector3 position)
    {
        var child = new GameObject(name).transform;
        child.SetParent(root.transform, false); child.localPosition = position; return child;
    }
    private void Label(string name, Vector3 position, string value, Font font)
    {
        TextMesh text = Anchor(name, position).gameObject.AddComponent<TextMesh>();
        text.font = font; text.text = value; text.fontSize = 48; text.characterSize = .006f;
        text.anchor = TextAnchor.MiddleCenter; text.color = new Color(.5f,.9f,1);
    }
    internal void Update(bool open, int page, int extent, int owned, int limit)
    {
        root.SetActive(open);
        if (ApplicationManager.IsHeadless) return;
        int count = SkillLimitPolicy.PageCount(extent);
        var ownedText = root.transform.Find("OwnedCount")?.GetComponent<TextMesh>();
        var pageText = root.transform.Find("PageCount")?.GetComponent<TextMesh>();
        if (ownedText != null) ownedText.text = owned + " / " + limit;
        if (pageText != null) pageText.text = (page + 1) + " / " + count;
        foreach (string button in new[] {"PreviousButton", "NextButton"})
            root.transform.Find(button)?.gameObject.SetActive(count > 1);
        for (int i = 0; i < 8; i++)
        {
            Transform marker = root.transform.Find("PageMarkers/Page_" + (i + 1));
            if (marker == null) continue;
            marker.gameObject.SetActive(i < count);
            marker.GetComponentInChildren<Renderer>(true).sharedMaterial = i == page ? glow : muted;
        }
    }
    internal void Destroy()
    {
        UnityEngine.Object.Destroy(root);
        if (glow != null) UnityEngine.Object.Destroy(glow);
        if (muted != null) UnityEngine.Object.Destroy(muted);
    }

    // The server still selects pages. Animate tracked hands on every client, but
    // send a haptic pulse only to the local shrine user's touching controller.
    internal void Feedback(PlayerController? player, bool enabled)
    {
        if (ApplicationManager.IsHeadless) return;
        Controller? left = player != null ? player.LeftController : null;
        Controller? right = player != null ? player.RightController : null;
        bool lp = enabled && Touches(left, Previous), rp = enabled && Touches(right, Previous);
        bool ln = enabled && Touches(left, Next), rn = enabled && Touches(right, Next);
        bool previous = lp || rp, next = ln || rn;
        bool touching = previous || next;
        if (touching && !wasTouching && previous != next && Time.unscaledTime >= nextPulse)
        {
            nextPulse = Time.unscaledTime + .5f;
            if (player != null && player == PlayerController.Current && player.IsLocalPlayer)
            {
                if (lp || ln) left?.TriggerHaptic(.35f, .045f, 90f);
                if (rp || rn) right?.TriggerHaptic(.35f, .045f, 90f);
            }
        }
        wasTouching = touching;
        Animate(previousFace, previousRest, previous && !next);
        Animate(nextFace, nextRest, next && !previous);
    }
    internal static bool Touches(Controller? hand, Transform target)
        => hand != null && (hand.transform.position - target.position).sqrMagnitude < .08f * .08f;
    private static void Animate(Transform? face, Vector3 rest, bool pressed)
    {
        if (face == null) return;
        // The face points along -Z; positive Z presses it into its fixed rim.
        face.localPosition = Vector3.MoveTowards(face.localPosition,
            rest + (pressed ? new Vector3(0, 0, .012f) : Vector3.zero), Time.unscaledDeltaTime * .12f);
    }
}
