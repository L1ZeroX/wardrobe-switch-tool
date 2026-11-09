#if RISV4_JSON
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YagihataItems.RadialInventorySystemV4
{
    public static class KaiThumbnail
    {
        internal static Bounds LastFramedBounds;
        internal static string[] LastVisibleRendererPaths = new string[0];
        internal static Bounds TightBounds(Renderer renderer)
        {
            var skin = renderer as SkinnedMeshRenderer;
            if (skin == null) return renderer.bounds;
            var mesh = new Mesh();
            try
            {
                skin.BakeMesh(mesh);
                var vertices = mesh.vertices;
                if (vertices.Length == 0) return renderer.bounds;
                // Mesh-deletion tools often retain unused body vertices. Only visible topology belongs in the framing bounds.
                var indices = mesh.triangles.Distinct().Where(i => i >= 0 && i < vertices.Length).ToArray();
                if (indices.Length == 0) return renderer.bounds;
                var bounds = new Bounds(skin.transform.TransformPoint(vertices[indices[0]]), Vector3.zero);
                foreach (var index in indices) bounds.Encapsulate(skin.transform.TransformPoint(vertices[index]));
                return bounds;
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
        public static Texture2D Capture(Avatar data, Prop prop, float yaw, float zoom, bool focusClothing = true)
        {
            return CaptureCore(data, prop, yaw, zoom, focusClothing, null);
        }
        // Caller owns this temporary texture. Previewing never writes PNGs or changes icon references.
        public static Texture2D PreviewSelection(Avatar data, System.Collections.Generic.IEnumerable<Prop> selection, bool showAvatar, float yaw = 0, float zoom = 1)
        {
            var items = selection.Where(p => p != null).Distinct().ToList();
            if (items.Count == 0) throw new InvalidOperationException("プレビューするプロップにチェックを入れてください。");
            var root = data.GetAvatarRoot();
            if (root == null || items.Any(p => p.TargetObjects.Count == 0 || p.TargetObjects.Any(t => t?.GetObject(root.gameObject) == null)))
                throw new InvalidOperationException("衣装の参照を確認してください。");
            var prop = new Prop { TargetObjects = items.SelectMany(p => p.TargetObjects).ToList() };
            var group = showAvatar ? new Group { Props = items } : null;
            return CaptureCore(data, prop, yaw, zoom, true, group, true);
        }
        public static Texture2D CaptureGroup(Avatar data, Group group)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            // Use a separate stable image ID; clothing thumbnails must never be overwritten.
            var id = group.KaiIconID;
            if (!string.IsNullOrEmpty(id) && (data.Groups.Any(g => !ReferenceEquals(g, group) && g.KaiIconID == id) || data.Groups.SelectMany(g => g.Props).Any(p => p.KaiIconID == id)))
                id = null;
            var image = new Prop { KaiIconID = id };
            var signature = GroupSignature(data, group);
            var icon = CaptureCore(data, image, 0, 1, false, group);
            group.KaiIconID = image.KaiIconID;
            group.Icon = image.Icon;
            group.KaiThumbnailSignature = signature;
            return icon;
        }
        internal static Prop[] GroupItems(Avatar data, Group group)
        {
            var items = new System.Collections.Generic.HashSet<Prop>();
            var pending = new System.Collections.Generic.Queue<Prop>(group.Props.Where(p => p != null));
            while (pending.Count > 0)
            {
                var item = pending.Dequeue();
                if (!items.Add(item)) continue;
                foreach (var pair in item.PropSets.Where(p => p != null))
                    if (pair.GroupIndex >= 0 && pair.GroupIndex < data.Groups.Count && pair.PropIndex >= 0 && pair.PropIndex < data.Groups[pair.GroupIndex].Props.Count)
                        pending.Enqueue(data.Groups[pair.GroupIndex].Props[pair.PropIndex]);
            }
            return items.ToArray();
        }
        internal static string GroupSignature(Avatar data, Group group)
        {
            var value = Newtonsoft.Json.JsonConvert.SerializeObject(new {
                revision = 3,
                avatar = data.AvatarRoot?.GetFormattedPath(),
                selected = GroupItems(data, group).Select(p => new { targets = p.TargetObjects.Select(r => r?.GetFormattedPath()).ToArray(), materials = p.MaterialOverrides.Select(m => m?.ObjectGUID).ToArray() }).ToArray(),
                allTargets = data.Groups.SelectMany(g => g.Props).SelectMany(p => p.TargetObjects).Select(r => r?.GetFormattedPath()).Distinct().OrderBy(p => p).ToArray()
            });
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value))).Replace("-", "");
        }
        internal static Transform[] GroupHiddenBranches(Avatar data, Group group, Transform root)
        {
            var selected = GroupItems(data, group);
            var selectedTargets = selected.SelectMany(p => p.TargetObjects).Select(r => r?.GetObject(root.gameObject)?.transform).Where(t => t != null).ToArray();
            var animator = root.GetComponent<Animator>();
            var hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            var branches = new System.Collections.Generic.HashSet<Transform>();
            foreach (var target in data.Groups.SelectMany(g => g.Props).Where(p => !selected.Contains(p)).SelectMany(p => p.TargetObjects).Select(r => r?.GetObject(root.gameObject)?.transform).Where(t => t != null && t != root))
            {
                if (selectedTargets.Any(t => t == target || t.IsChildOf(target))) continue;
                var branch = target;
                // Include accessories belonging to the other outfit, but do not cross a
                // shared clothing container or the avatar's skeleton root.
                while (branch.parent != null && branch.parent != root && !selectedTargets.Any(t => t == branch.parent || t.IsChildOf(branch.parent)) && !(hips != null && (hips == branch.parent || hips.IsChildOf(branch.parent))))
                    branch = branch.parent;
                branches.Add(branch);
            }
            return branches.ToArray();
        }
        sealed class HeelPreview
        {
            public KaiHeelLink.Link Link;
            public bool Worn, Feet;
            public float Pitch;
        }
        // Return an owned temporary texture; no source renderer, icon or asset is edited.
        public static Texture2D PreviewHeel(Avatar data, Prop original, Prop settings, bool worn, bool feet, float yaw, float pitch, float zoom)
        {
            var group = data.Groups.FirstOrDefault(g => g.Props.Contains(original));
            if (group == null) throw new InvalidOperationException("衣装が削除されています。調整画面を開き直してください。");
            var link = KaiHeelLink.Resolve(data, settings);
            if (link == null) throw new InvalidOperationException("連動を自動または手動指定にして、対象の靴を選んでください。");
            if (link.Error != null) throw new InvalidOperationException(link.Error);
            return CaptureCore(data, settings, yaw, zoom, false, group, true,
                new HeelPreview { Link = link, Worn = worn, Feet = feet, Pitch = pitch });
        }
        static Texture2D CaptureCore(Avatar data, Prop prop, float yaw, float zoom, bool focusClothing, Group fullGroup, bool previewOnly = false, HeelPreview heel = null)
        {
            bool fullAvatar = fullGroup != null;
            var original = data.GetAvatarRoot();
            if (original == null) throw new InvalidOperationException("アバターを指定してください。");
            var preview = new PreviewRenderUtility();
            GameObject clone = null;
            Texture2D texture = null;
            bool previewOpen = false;
            var previous = RenderTexture.active;
            try
            {
                clone = UnityEngine.Object.Instantiate(original.gameObject);
                clone.name = original.gameObject.name;
                clone.hideFlags = HideFlags.HideAndDontSave;
                preview.AddSingleGO(clone);
                // Unity includes null entries for missing scripts. Leave the source intact
                // and skip these entries when disabling behaviours on the preview clone.
                foreach (var behaviour in clone.GetComponentsInChildren<Behaviour>(true))
                    if (behaviour != null) behaviour.enabled = false;
                foreach (var renderer in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true)) renderer.updateWhenOffscreen = true;
                clone.transform.position = Vector3.zero;
                clone.transform.rotation = Quaternion.identity;
                // Resolve on the original, then use transform paths on the isolated preview copy.
                Action<Prop, bool> apply = (item, enabled) =>
                {
                    foreach (var reference in item.TargetObjects)
                    {
                        var source = reference?.GetObject(original.gameObject);
                        if (source == null) continue;
                        var path = AnimationUtility.CalculateTransformPath(source.transform, original.transform);
                        var target = string.IsNullOrEmpty(path) ? clone.transform : clone.transform.Find(path);
                        if (target == null) continue;
                        target.gameObject.SetActive(enabled);
                        if (enabled)
                        {
                            for (var ancestor = target.parent; ancestor != null && ancestor != clone.transform; ancestor = ancestor.parent)
                                ancestor.gameObject.SetActive(true);
                            var renderer = target.GetComponent<Renderer>();
                            if (renderer != null && item.MaterialOverrides.Any(m => m?.GetObject() != null))
                            {
                                var materials = renderer.sharedMaterials;
                                for (int m = 0; m < Math.Min(materials.Length, item.MaterialOverrides.Count); m++)
                                    if (item.MaterialOverrides[m]?.GetObject() != null) materials[m] = item.MaterialOverrides[m].GetObject();
                                renderer.sharedMaterials = materials;
                            }
                        }
                    }
                };
                if (!fullAvatar)
                {
                    foreach (var item in data.Groups.SelectMany(g => g.Props))
                        apply(item, item.IsDefaultEnabled && (prop.ExclusiveGroup == RIS.ExclusiveGroupType.None || item.ExclusiveGroup != prop.ExclusiveGroup));
                    apply(prop, true);
                    foreach (var pair in prop.PropSets.Where(p => p != null))
                        if (pair.GroupIndex >= 0 && pair.GroupIndex < data.Groups.Count && pair.PropIndex >= 0 && pair.PropIndex < data.Groups[pair.GroupIndex].Props.Count)
                            apply(data.Groups[pair.GroupIndex].Props[pair.PropIndex], true);
                }
                else
                {
                    // Isolate this outfit on the copy. Unregistered body/hair retain their
                    // existing state, while clothes belonging to other groups are hidden.
                    foreach (var item in data.Groups.SelectMany(g => g.Props)) apply(item, false);
                    foreach (var item in GroupItems(data, fullGroup)) apply(item, true);
                    foreach (var branch in GroupHiddenBranches(data, fullGroup, original.transform))
                    {
                        var path = AnimationUtility.CalculateTransformPath(branch, original.transform);
                        var otherOutfit = clone.transform.Find(path);
                        if (otherOutfit != null)
                            foreach (var renderer in otherOutfit.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                    }
                }
                if (heel != null)
                {
                    // Other shoes in this outfit must not obscure the selected pair.
                    foreach (var item in fullGroup.Props)
                        if (KaiHeelLink.Resolve(data, item) != null) apply(item, false);
                    apply(prop, heel.Worn);
                    var path = AnimationUtility.CalculateTransformPath(heel.Link.Renderer.transform, original.transform);
                    var body = (string.IsNullOrEmpty(path) ? clone.transform : clone.transform.Find(path)).GetComponent<SkinnedMeshRenderer>();
                    body.SetBlendShapeWeight(body.sharedMesh.GetBlendShapeIndex(heel.Link.Shape), heel.Worn ? heel.Link.Weight : 0);
                }
                clone.SetActive(true);
                foreach (var node in clone.GetComponentsInChildren<Transform>(true))
                    if (node.CompareTag("EditorOnly")) node.gameObject.SetActive(false);
                var renderers = clone.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                if (renderers.Length == 0) throw new InvalidOperationException("撮影対象のRendererがありません。");
                // Use the selected clothing's actual vertices: imported skinned renderer bounds often cover the whole avatar.
                var framing = renderers.OfType<SkinnedMeshRenderer>().Cast<Renderer>().ToArray();
                if (framing.Length == 0) framing = renderers;
                if (!fullAvatar)
                {
                    var targets = new System.Collections.Generic.HashSet<Renderer>();
                    var items = new System.Collections.Generic.List<Prop> { prop };
                    foreach (var pair in prop.PropSets.Where(p => p != null))
                        if (pair.GroupIndex >= 0 && pair.GroupIndex < data.Groups.Count && pair.PropIndex >= 0 && pair.PropIndex < data.Groups[pair.GroupIndex].Props.Count)
                            items.Add(data.Groups[pair.GroupIndex].Props[pair.PropIndex]);
                    foreach (var item in items)
                        foreach (var reference in item.TargetObjects)
                        {
                            var source = reference?.GetObject(original.gameObject);
                            if (source == null) continue;
                            if (source == original.gameObject)
                                throw new InvalidOperationException("アバター全体ではなく、撮影する衣装オブジェクトを指定してください。");
                            var path = AnimationUtility.CalculateTransformPath(source.transform, original.transform);
                            var target = string.IsNullOrEmpty(path) ? clone.transform : clone.transform.Find(path);
                            if (target != null)
                                foreach (var renderer in target.GetComponentsInChildren<Renderer>())
                                    if (renderer.enabled) targets.Add(renderer);
                        }
                    if (targets.Count == 0)
                        throw new InvalidOperationException("選択した衣装に表示可能なRendererがありません。対象オブジェクトを確認してください。");
                    // Keep the skeleton for skinning, but render only this clothing (and its set).
                    // Never hide source objects or include unrelated body/hair/other outfits.
                    foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
                        if (!targets.Contains(renderer)) renderer.enabled = false;
                    if (focusClothing) framing = targets.ToArray();
                    LastVisibleRendererPaths = targets.Select(r => AnimationUtility.CalculateTransformPath(r.transform, clone.transform)).OrderBy(p => p).ToArray();
                }
                else LastVisibleRendererPaths = renderers.Select(r => AnimationUtility.CalculateTransformPath(r.transform, clone.transform)).OrderBy(p => p).ToArray();
                var bounds = TightBounds(framing[0]);
                foreach (var renderer in framing.Skip(1)) bounds.Encapsulate(TightBounds(renderer));
                if (heel != null && heel.Feet)
                {
                    var path = AnimationUtility.CalculateTransformPath(heel.Link.Renderer.transform, original.transform);
                    var body = (string.IsNullOrEmpty(path) ? clone.transform : clone.transform.Find(path)).GetComponent<SkinnedMeshRenderer>();
                    var bodyBounds = TightBounds(body);
                    // Frame the lower quarter of the body even when the shoes are hidden.
                    // This also works with differently named meshes and non-humanoid rigs.
                    float height = Mathf.Max(bodyBounds.size.y * .25f, .08f);
                    bounds = new Bounds(new Vector3(bodyBounds.center.x, bodyBounds.min.y + height * .5f, bodyBounds.center.z),
                        new Vector3(Mathf.Min(bodyBounds.size.x, height * 1.6f), height, Mathf.Min(bodyBounds.size.z, height)));
                    foreach (var reference in prop.TargetObjects)
                    {
                        var source = reference?.GetObject(original.gameObject);
                        if (source == null) continue;
                        var target = clone.transform.Find(AnimationUtility.CalculateTransformPath(source.transform, original.transform));
                        if (target != null) foreach (var renderer in target.GetComponentsInChildren<Renderer>(true)) bounds.Encapsulate(TightBounds(renderer));
                    }
                    bounds.Expand(height * .12f);
                }
                LastFramedBounds = bounds;
                var camera = preview.camera;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0, 0, 0, 0);
                camera.allowHDR = false;
                preview.ambientColor = new Color(0.7f, 0.7f, 0.7f, 1);
                camera.fieldOfView = 30;
                // A bounding sphere also accounts for the near side of deep garments/shoes in perspective.
                float distance = Mathf.Max(0.001f, bounds.extents.magnitude) / Mathf.Sin(15 * Mathf.Deg2Rad) * 1.05f / Mathf.Clamp(zoom, 0.5f, 3f);
                camera.transform.position = bounds.center + Quaternion.Euler(heel == null ? 0 : heel.Pitch, yaw, 0) * Vector3.forward * distance;
                camera.transform.LookAt(bounds.center);
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = distance + bounds.size.magnitude * 3 + 10;
                preview.lights[0].intensity = 1.2f;
                preview.lights[0].transform.rotation = Quaternion.Euler(40, 35, 0);
                preview.lights[1].intensity = 0.8f;
                preview.lights[1].transform.rotation = Quaternion.Euler(340, 215, 0);
                // EndStaticPreview returns RGB24 and discards alpha. Read the live preview
                // into RGBA32 instead, with an explicit sRGB conversion for linear projects.
                preview.BeginPreview(new Rect(0, 0, 512, 512), GUIStyle.none);
                previewOpen = true;
                preview.Render();
                var rendered = preview.EndPreview();
                previewOpen = false;
                var output = RenderTexture.GetTemporary(512, 512, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var previousSRGB = GL.sRGBWrite;
                try
                {
                    GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                    Graphics.Blit(rendered, output);
                    RenderTexture.active = output;
                    texture = new Texture2D(512, 512, TextureFormat.RGBA32, false, false);
                    texture.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                    texture.Apply();
                }
                finally
                {
                    GL.sRGBWrite = previousSRGB;
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(output);
                }
                if (previewOnly)
                {
                    texture.hideFlags = HideFlags.HideAndDontSave;
                    var result = texture;
                    texture = null; // Transfer ownership; the window destroys it on replacement/close.
                    return result;
                }
                string dir = RIS.WorkFolderPath + "KaiIcons/";
                Directory.CreateDirectory(dir);
                Guid iconID;
                if (!Guid.TryParse(prop.KaiIconID, out iconID)) iconID = Guid.NewGuid();
                prop.KaiIconID = iconID.ToString("N");
                string pathOut = dir + prop.KaiIconID + ".png";
                // Encode and write before replacing the existing icon reference.
                byte[] previousImage = File.Exists(pathOut) ? File.ReadAllBytes(pathOut) : null;
                Directory.CreateDirectory("Temp");
                string staging = "Temp/RI-Kai-" + prop.KaiIconID + ".tmp";
                Texture2D icon;
                try
                {
                    File.WriteAllBytes(staging, texture.EncodeToPNG());
                    if (File.Exists(pathOut)) File.Replace(staging, pathOut, null);
                    else File.Move(staging, pathOut);
                    AssetDatabase.ImportAsset(pathOut, ImportAssetOptions.ForceSynchronousImport);
                    var importer = (TextureImporter)AssetImporter.GetAtPath(pathOut);
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    KaiIconImport.Configure(importer);
                    importer.SaveAndReimport();
                    icon = AssetDatabase.LoadAssetAtPath<Texture2D>(pathOut);
                    if (icon == null) throw new IOException("撮影画像の読み込みに失敗しました。");
                }
                catch
                {
                    if (previousImage != null) { File.WriteAllBytes(pathOut, previousImage); AssetDatabase.ImportAsset(pathOut); }
                    else if (File.Exists(pathOut)) AssetDatabase.DeleteAsset(pathOut);
                    throw;
                }
                prop.Icon = new GUIDPathPair<Texture2D>(ObjectPathStateType.Asset, icon);
                return icon;
            }
            finally
            {
                if (previewOpen) preview.EndPreview();
                RenderTexture.active = previous;
                preview.camera.targetTexture = null;
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                preview.Cleanup();
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
            }
        }
    }
}
#endif
