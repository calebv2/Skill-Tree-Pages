using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Stream = Alta.Serialization.Stream;

namespace RevampedSkills.Progression;

internal static class ShrineAccess
{
    internal static readonly FieldInfo Holders = AccessTools.Field(typeof(BoughtSkillVisualizer), "placedHolders");
    internal static readonly Type HandleType = AccessTools.Inner(typeof(BoughtSkillVisualizer), "OrbHolderHandle");
    internal static readonly FieldInfo Holder = AccessTools.Field(HandleType, "holder");
    internal static readonly PropertyInfo Orb = AccessTools.Property(HandleType, "Orb");
    internal static readonly PropertyInfo Hash = AccessTools.Property(HandleType, "SlotHash");
    internal static readonly FieldInfo Shrine = AccessTools.Field(typeof(BoughtSkillVisualizer), "fillVisualizerObject");
    internal static readonly FieldInfo Radius = AccessTools.Field(typeof(BoughtSkillVisualizer), "radius");
    internal static readonly FieldInfo Offset = AccessTools.Field(typeof(BoughtSkillVisualizer), "verticalOffset");
    internal static readonly FieldInfo Rotation = AccessTools.Field(typeof(BoughtSkillVisualizer), "offsetRotation");
    internal static readonly MethodInfo Place = AccessTools.Method(typeof(BoughtSkillVisualizer), "PlaceOrbHolders");
    internal static readonly MethodInfo Interacting = AccessTools.Method(typeof(BoughtSkillVisualizer), "IsBeingInteracted");
    internal static readonly MethodInfo Update = AccessTools.Method(typeof(ProgressionShrine), "UpdateVisuals");
}

[HarmonyPatch]
internal static class RefreshAssignedOrbPage
{
    private static MethodBase TargetMethod() => AccessTools.Method(ShrineAccess.HandleType, "AssignOrb");
    private static void Postfix(BoughtSkillOrbReplacer __0)
    {
        ShrinePages pages = __0.GetComponentInParent<ShrinePages>();
        if (pages != null) pages.Dirty = true;
    }
}

[HarmonyPatch(typeof(BoughtSkillOrbReplacer), "FilterInteraction")]
internal static class PreventHiddenOrbInteraction
{
    private static void Postfix(BoughtSkillOrbReplacer __instance, ref bool __2)
    {
        ShrinePages pages = __instance.GetComponentInParent<ShrinePages>();
        if (pages != null && !pages.IsVisible(__instance)) __2 = false;
    }
}

[HarmonyPatch(typeof(BoughtSkillVisualizer), "Awake")]
internal static class PreparePagedHolders
{
    private static void Prefix(BoughtSkillVisualizer __instance, ref int ___crystalCount)
    {
        // Fixed wire capacity prevents server/client cap changes from changing packet length.
        ___crystalCount = SkillLimitPolicy.Capacity;
        Array? holders = ShrineAccess.Holders.GetValue(__instance) as Array;
        if (holders != null && holders.Length != SkillLimitPolicy.Capacity)
            ShrineAccess.Place.Invoke(__instance, null);
    }
    private static void Postfix(BoughtSkillVisualizer __instance)
        => ShrinePages.Get(__instance).Initialize(__instance);
}

[HarmonyPatch(typeof(BoughtSkillVisualizer), "PrepareOnServer")]
internal static class PreparePageOwner
{
    private static void Postfix(BoughtSkillVisualizer __instance, PlayerProgressionManager __0)
        => ShrinePages.Get(__instance).SetOwner(__0);
}

[HarmonyPatch(typeof(BoughtSkillVisualizer), "Serialize")]
internal static class SyncShrinePages
{
    private static void Prefix(BoughtSkillVisualizer __instance, Stream __0)
    {
        ShrinePages pages = ShrinePages.Get(__instance);
        uint limit = NetworkSceneManager.IsServer ? (uint)SkillLimitConfig.ServerLimit : 8u;
        uint page = (uint)pages.Page;
        uint extent = (uint)pages.Extent;
        __0.SerializeUnsignedInt(ref limit);
        __0.SerializeUnsignedInt(ref page);
        __0.SerializeUnsignedInt(ref extent);
        if (__0.IsReading && !NetworkSceneManager.IsServer)
        {
            if (limit < 1 || limit > SkillLimitPolicy.Capacity || extent < limit ||
                extent > SkillLimitPolicy.Capacity || page >= SkillLimitPolicy.PageCount((int)extent))
                throw new InvalidOperationException("Invalid RevampedSkills shrine page data. Server and client must use the same version.");
            SkillLimitConfig.RemoteLimit = (int)limit;
            pages.Receive((int)page, (int)extent);
        }
    }
}

[HarmonyPatch(typeof(BoughtSkillVisualizer), "Disable")]
internal static class ResetShrinePages
{
    private static void Prefix(BoughtSkillVisualizer __instance) => ShrinePages.Get(__instance).ResetPage();
}

// These components are local UI only. The server observes the shrine user's tracked
// hands, chooses the page, then sends it through the shrine's existing visual sync.
internal sealed class ShrinePages : MonoBehaviour
{
    internal int Page { get; private set; }
    internal int Extent { get; private set; } = 8;
    private BoughtSkillVisualizer visualizer = null!;
    private PlayerProgressionManager? owner;
    private Transform? previous;
    private Transform? next;
    private TextMesh? counter;
    private ShrineAttachmentView? attachment;
    private bool touching;
    private float nextTouch;
    private bool initialized;
    internal bool Dirty = true;
    private bool wasOpen;
    private readonly Dictionary<Renderer, bool> hiddenRenderers = new();
    private readonly Dictionary<Collider, bool> hiddenColliders = new();

    internal static ShrinePages Get(BoughtSkillVisualizer visualizer)
        => visualizer.GetComponent<ShrinePages>() ?? visualizer.gameObject.AddComponent<ShrinePages>();

    internal bool IsVisible(BoughtSkillOrbReplacer orb)
    {
        if (!initialized || ShrineAccess.Shrine.GetValue(visualizer) is not ProgressionShrine shrine || !shrine.IsOpen)
            return false;
        Array? handles = ShrineAccess.Holders.GetValue(visualizer) as Array;
        if (handles != null)
            for (int i = 0; i < handles.Length; i++)
                if (handles.GetValue(i) is object handle && ReferenceEquals(ShrineAccess.Orb.GetValue(handle, null), orb))
                    return SkillLimitPolicy.Visible(i, Page, Extent);
        return false;
    }

    internal void Initialize(BoughtSkillVisualizer target)
    {
        if (initialized) return;
        initialized = true;
        visualizer = target;
        Array? handles = ShrineAccess.Holders.GetValue(target) as Array;
        float radius = (float)ShrineAccess.Radius.GetValue(target);
        float vertical = (float)ShrineAccess.Offset.GetValue(target);
        Quaternion rotation = (Quaternion)ShrineAccess.Rotation.GetValue(target);
        if (handles != null)
        {
            for (int i = 0; i < handles.Length; i++)
            {
                if (handles.GetValue(i) is not object handle || ShrineAccess.Holder.GetValue(handle) is not Component holder) continue;
                Vector3 radial = Quaternion.AngleAxis(-180 + 360f * (i % SkillLimitPolicy.PageSize) /
                    SkillLimitPolicy.PageSize, target.transform.forward) * target.transform.up * radius;
                holder.transform.SetPositionAndRotation(target.transform.position + radial + Vector3.up * vertical,
                    Quaternion.LookRotation(-radial, Vector3.up) * rotation);
            }
        }
        attachment = ShrineAttachmentView.Create(target.transform);
        if (attachment != null) { previous = attachment.Previous; next = attachment.Next; }
        else {
        previous = MakeLabel("Previous", new Vector3(-radius - 0.13f, 0, 0));
        next = MakeLabel("Next", new Vector3(radius + 0.13f, 0, 0));
        counter = MakeLabel("Skills", new Vector3(0, -radius - 0.14f, 0)).GetComponent<TextMesh>();
        }
        ApplyVisibility(false);
    }

    private Transform MakeLabel(string text, Vector3 position)
    {
        GameObject label = new("RevampedSkills " + text);
        label.transform.SetParent(transform, false);
        label.transform.localPosition = position;
        // Headless instances still need the same touch target transforms.
        if (!ApplicationManager.IsHeadless)
        {
            TextMesh mesh = label.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            mesh.font = font;
            label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            mesh.text = text;
            mesh.fontSize = 48;
            mesh.characterSize = 0.008f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = new Color(0.5f, 0.9f, 1f);
        }
        return label.transform;
    }

    internal void SetOwner(PlayerProgressionManager manager)
    {
        Dirty = true;
        Initialize(GetComponent<BoughtSkillVisualizer>());
        if (owner != manager) { Page = 0; touching = true; }
        owner = manager;
        Extent = SkillLimitConfig.ServerLimit;
        Array? handles = ShrineAccess.Holders.GetValue(visualizer) as Array;
        if (handles != null)
            for (int i = 0; i < handles.Length; i++)
                if (handles.GetValue(i) is object handle && (uint)ShrineAccess.Hash.GetValue(handle, null) != 0)
                    Extent = Math.Max(Extent, i + 1);
        Page = Math.Min(Page, SkillLimitPolicy.PageCount(Extent) - 1);
    }

    internal void Receive(int page, int extent)
    {
        Dirty = true;
        Initialize(GetComponent<BoughtSkillVisualizer>());
        Page = page;
        Extent = extent;
    }

    internal void ResetPage()
    {
        Dirty = true;
        Page = 0;
        owner = null;
        touching = true;
        RestoreHidden();
    }

    private void LateUpdate()
    {
        if (!initialized || visualizer == null) return;
        ProgressionShrine? shrine = ShrineAccess.Shrine.GetValue(visualizer) as ProgressionShrine;
        bool open = shrine != null && shrine.IsOpen;
        PlayerProgressionManager? activeManager = shrine != null ? shrine.CurrentManager : null;
        PlayerController? activePlayer = activeManager != null ? activeManager.Player.PlayerController : null;
        attachment?.Feedback(activePlayer, open && SkillLimitPolicy.PageCount(Extent) > 1 &&
            !(bool)ShrineAccess.Interacting.Invoke(visualizer, null));
        if (Dirty || open != wasOpen)
        {
            ApplyVisibility(open);
            Dirty = false;
            wasOpen = open;
        }
        if (counter != null)
        {
            counter.text = $"Skills {Page + 1} / {SkillLimitPolicy.PageCount(Extent)}\nTouch Previous / Next";
            PlayerController player = PlayerController.Current;
            if (player != null && player.Head != null)
            {
                previous!.rotation = Quaternion.LookRotation(previous.position - player.Head.position);
                next!.rotation = Quaternion.LookRotation(next.position - player.Head.position);
                counter.transform.rotation = Quaternion.LookRotation(counter.transform.position - player.Head.position);
            }
        }
        if (!open || !NetworkSceneManager.IsServer || owner == null || SkillLimitPolicy.PageCount(Extent) <= 1) return;
        PlayerController controller = owner.Player.PlayerController;
        if (controller == null) return;
        bool prev = IsTouching(controller, previous!);
        bool forward = IsTouching(controller, next!);
        bool entered = (prev || forward) && !touching;
        touching = prev || forward;
        if (!entered || prev == forward || Time.unscaledTime < nextTouch ||
            (bool)ShrineAccess.Interacting.Invoke(visualizer, null)) return;
        Page = SkillLimitPolicy.MovePage(Page, forward ? 1 : -1, Extent);
        nextTouch = Time.unscaledTime + 0.5f;
        ApplyVisibility(true);
        ShrineAccess.Update.Invoke(shrine, new object[] { owner.Player, visualizer.Path });
    }

    private static bool IsTouching(PlayerController player, Transform target)
    {
        return ShrineAttachmentView.Touches(player.LeftController, target) ||
            ShrineAttachmentView.Touches(player.RightController, target);
    }

    private void ApplyVisibility(bool open)
    {
        if (previous != null) previous.gameObject.SetActive(open && Extent > SkillLimitPolicy.PageSize);
        if (next != null) next.gameObject.SetActive(open && Extent > SkillLimitPolicy.PageSize);
        if (counter != null) counter.gameObject.SetActive(open);
        RestoreHidden();
        Array? handles = ShrineAccess.Holders.GetValue(visualizer) as Array;
        int owned = 0;
        if (handles != null)
            foreach (object handle in handles)
                if ((uint)ShrineAccess.Hash.GetValue(handle, null) != 0) owned++;
        attachment?.Update(open, Page, Extent, owned,
            NetworkSceneManager.IsServer ? SkillLimitConfig.ServerLimit : (SkillLimitConfig.RemoteLimit ?? 8));
        if (handles == null) return;
        for (int i = 0; i < handles.Length; i++)
        {
            if (open && SkillLimitPolicy.Visible(i, Page, Extent)) continue;
            if (handles.GetValue(i) is not object handle || ShrineAccess.Holder.GetValue(handle) is not Component holder) continue;
            // Keep network objects and their scripts alive. Only rendering and physical
            // interaction are hidden; the existing joint/skill/save state is untouched.
            foreach (Renderer renderer in holder.GetComponentsInChildren<Renderer>(true))
            { hiddenRenderers[renderer] = renderer.enabled; renderer.enabled = false; }
            foreach (Collider collider in holder.GetComponentsInChildren<Collider>(true))
            { hiddenColliders[collider] = collider.enabled; collider.enabled = false; }
        }
    }

    private void RestoreHidden()
    {
        foreach (var entry in hiddenRenderers) if (entry.Key != null) entry.Key.enabled = entry.Value;
        foreach (var entry in hiddenColliders) if (entry.Key != null) entry.Key.enabled = entry.Value;
        hiddenRenderers.Clear();
        hiddenColliders.Clear();
    }

    private void OnDestroy() { RestoreHidden(); attachment?.Destroy(); }
}

