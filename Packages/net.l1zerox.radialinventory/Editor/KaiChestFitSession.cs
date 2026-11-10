#if RISV4_JSON
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YagihataItems.RadialInventorySystemV4
{
    public enum KaiFitBrush { Correct, Push, Restore }
    public sealed class KaiChestFitSession : IDisposable
    {
        sealed class Part
        {
            public SkinnedMeshRenderer Source, Copy;
            public Mesh Original, Working;
            public Vector3[] Basis, Offsets, Initial, Points, Normals, BrushNormals, ReferencePoints;
            public Matrix4x4[] InverseSkin;
            public KaiFitSurface Surface, ReferenceSurface;
            public KaiFitSeams Seams;
        }
        public readonly GameObject Root;
        public readonly SkinnedMeshRenderer Body;
        public SkinnedMeshRenderer Clothing => parts[0].Source;
        public SkinnedMeshRenderer[] Targets => parts.Select(p=>p.Source).ToArray();
        public Mesh Original => parts[0].Original;
        public Vector3[] Offsets => parts[0].Offsets;
        public readonly string Shape;
        public readonly float InitialValue;
        public float Value { get; private set; }
        public Bounds FocusBounds { get; private set; }
        public bool Changed => parts.Any(p=>p.Offsets.Where((v,i)=>(v-p.Initial[i]).sqrMagnitude>1e-14f).Any()) || Mathf.Abs(Value-InitialValue)>.0001f;
        public int ChangedVertices => parts.Sum(p=>p.Offsets.Count(v=>v.sqrMagnitude>1e-14f));
        public int UndoCount => undo.Count;
        readonly List<Part> parts=new List<Part>();
        readonly Stack<Vector3[][]> undo=new Stack<Vector3[][]>(), redo=new Stack<Vector3[][]>();
        readonly string sourceStamp;
        PreviewRenderUtility preview;
        GameObject clone;
        SkinnedMeshRenderer bodyCopy;
        KaiFitSurface bodySurface;
        KaiFitLayers layers;
        bool[][] layerAllowed;
        Vector3 layerCamera;
        public bool PreserveLayers=true;
        public float LayerGap=.002f;
        public SkinnedMeshRenderer[] LayerOrder=>layers.Order.Select(i=>parts[i].Source).ToArray();
        public int LayerConstraintCount=>layers.ConstraintCount;
        public void AutoLayerOrder()=>layers.SetOrder(layers.DetectOrder());
        public void SetLayerOrder(SkinnedMeshRenderer[] order)=>layers.SetOrder(order.Select(r=>parts.FindIndex(p=>p.Source==r)).ToArray());
        bool disposed;

        public KaiChestFitSession(GameObject root,SkinnedMeshRenderer body,SkinnedMeshRenderer clothing,string shape)
            : this(root,body,new[]{clothing},shape) { }
        public KaiChestFitSession(GameObject root,SkinnedMeshRenderer body,IEnumerable<SkinnedMeshRenderer> clothing,string shape)
        {
            Root=root; Body=body; Shape=shape??"";
            var targets=(clothing??Enumerable.Empty<SkinnedMeshRenderer>()).Distinct().ToArray();
            if(root==null||body==null||!body.transform.IsChildOf(root.transform)||targets.Length==0 ||
                targets.Any(r=>r==null||r==body||!r.transform.IsChildOf(root.transform)))
                throw new InvalidOperationException("同じアバター内の素体と衣装を別々に選んでください。");
            if(body.sharedMesh==null||targets.Any(r=>r.sharedMesh==null))throw new InvalidOperationException("メッシュがありません。");
            if(targets.Select(r=>r.gameObject).Distinct().Count()!=targets.Length)
                throw new InvalidOperationException("同じオブジェクトに複数のレンダラーがあります。別々に補正してください。");
            int si=string.IsNullOrEmpty(Shape)?-1:body.sharedMesh.GetBlendShapeIndex(Shape);
            if(!string.IsNullOrEmpty(Shape)&&si<0)throw new InvalidOperationException("胸のシェイプキーを選び直してください。");
            InitialValue=Value=si<0?0:body.GetBlendShapeWeight(si);
            foreach(var r in targets)
            {
                var record=r.GetComponent<KaiChestFitRecord>();
                if(record!=null && (record.Clothing!=r || (r.sharedMesh!=record.OriginalMesh && r.sharedMesh!=record.CorrectedMesh)))
                    throw new InvalidOperationException(r.name+"：補正後に別のツールでメッシュが変更されています。復元タブで状態を確認してください。");
                var original=record!=null?record.OriginalMesh:r.sharedMesh;
                if(original==null)throw new InvalidOperationException(r.name+"：復元元メッシュが見つかりません。");
                var basis=original.vertices;var current=r.sharedMesh.vertices;
                if(current.Length!=basis.Length)throw new InvalidOperationException("元と現在の頂点数が異なります。");
                var offsets=current.Select((v,i)=>v-basis[i]).ToArray();
                parts.Add(new Part {Source=r,Original=original,Basis=basis,Offsets=offsets,Initial=(Vector3[])offsets.Clone()});
            }
            sourceStamp=Stamp();
            try
            {
                preview=new PreviewRenderUtility();clone=UnityEngine.Object.Instantiate(root);clone.hideFlags=HideFlags.HideAndDontSave;preview.AddSingleGO(clone);
                foreach(var b in clone.GetComponentsInChildren<Behaviour>(true))if(b!=null)b.enabled=false;
                foreach(var r in clone.GetComponentsInChildren<Renderer>(true))r.enabled=false;
                bodyCopy=Find(body);Enable(bodyCopy);
                foreach(var p in parts)
                {
                    p.Copy=Find(p.Source);Enable(p.Copy);
                    p.Working=UnityEngine.Object.Instantiate(p.Original);p.Working.hideFlags=HideFlags.HideAndDontSave;p.Copy.sharedMesh=p.Working;
                    for(int i=0;i<p.Working.blendShapeCount;i++)p.Copy.SetBlendShapeWeight(i,p.Source.GetBlendShapeWeight(i));
                    p.InverseSkin=InverseSkinMatrices(p.Copy);
                    Vector3[] basePoints,baseNormals;Bake(p.Copy,out basePoints,out baseNormals);
                    p.Seams=new KaiFitSeams(p.Original);
                    RefreshWorking(p);p.BrushNormals=baseNormals;
                    p.ReferencePoints=(Vector3[])p.Points.Clone();p.ReferenceSurface=new KaiFitSurface(p.ReferencePoints,p.BrushNormals,p.Working.triangles);
                }
                RefreshBody();
                layers=new KaiFitLayers(parts.Select(p=>p.ReferencePoints).ToArray(),parts.Select(p=>p.BrushNormals).ToArray(),parts.Select(p=>p.Working.triangles).ToArray());
            }
            catch {Dispose();throw;}
        }
        void Enable(SkinnedMeshRenderer r)
        {
            r.enabled=true;r.updateWhenOffscreen=true;
            for(var t=r.transform;t!=null&&t.IsChildOf(clone.transform);t=t.parent)t.gameObject.SetActive(true);
        }
        SkinnedMeshRenderer Find(SkinnedMeshRenderer source)
        {
            // Sibling indices also distinguish outfits with duplicate object names.
            var indices=new Stack<int>();
            for(var t=source.transform;t!=Root.transform;t=t.parent)indices.Push(t.GetSiblingIndex());
            var target=clone.transform;while(indices.Count>0)target=target.GetChild(indices.Pop());
            return target.GetComponents<SkinnedMeshRenderer>()[Array.IndexOf(source.GetComponents<SkinnedMeshRenderer>(),source)];
        }
        string Stamp()
        {
            var renderers=new[]{Body}.Concat(parts.Select(p=>p.Source)).ToArray();
            if(renderers.Any(r=>r==null||r.sharedMesh==null))return "missing";
            return QualitySettings.skinWeights+"|"+string.Join("|",renderers.Select(r=>
                r.sharedMesh.GetInstanceID()+":"+AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(r.sharedMesh))+":"+r.quality+":"+
                r.transform.localToWorldMatrix.ToString("R")+":"+
                string.Join(",",Enumerable.Range(0,r.sharedMesh.blendShapeCount).Select(i=>r.GetBlendShapeWeight(i).ToString("R")))+":"+
                string.Join(";",r.bones.Select(b=>b==null?"null":b.localToWorldMatrix.ToString("R")))));
        }
        public void RequireUnchangedSource()
        {
            if(disposed||Root==null||Body==null||Clothing==null||Stamp()!=sourceStamp)
                throw new InvalidOperationException("編集中に元アバターが変更されました。画面の対象を選び直し、最新状態から補正してください。");
        }
        static bool UseScale(SkinnedMeshRenderer r)=>r.bones.Length>0 && r.sharedMesh.bindposes.Length>0;
        static Matrix4x4[] InverseSkinMatrices(SkinnedMeshRenderer r)
        {
            // Measure the linear skinning transform on the disposable copy. This follows
            // Unity's actual weight limit and scaled/rotated bones instead of assuming
            // that all stored bone weights contribute to BakeMesh equally.
            var mesh=r.sharedMesh;var basis=mesh.vertices;var result=new Matrix4x4[basis.Length];
            var baked=new Mesh();const float step=.01f;
            try
            {
                r.BakeMesh(baked,UseScale(r));var initial=baked.vertices;
                for(int i=0;i<result.Length;i++)result[i]=Matrix4x4.identity;
                for(int axis=0;axis<3;axis++)
                {
                    var offset=Vector3.zero;offset[axis]=step;
                    mesh.vertices=basis.Select(v=>v+offset).ToArray();r.BakeMesh(baked,UseScale(r));var moved=baked.vertices;
                    for(int i=0;i<result.Length;i++)
                    {
                        var d=r.transform.localToWorldMatrix.MultiplyVector((moved[i]-initial[i])/step);
                        result[i].SetColumn(axis,new Vector4(d.x,d.y,d.z,0));
                    }
                }
                for(int i=0;i<result.Length;i++)
                {
                    if(Mathf.Abs(result[i].determinant)<1e-10f)throw new InvalidOperationException("ゼロスケールなどで変形を戻せないボーンがあります。");
                    result[i]=result[i].inverse;
                }
                return result;
            }
            finally {mesh.vertices=basis;UnityEngine.Object.DestroyImmediate(baked);}
        }
        static KaiFitSurface Bake(SkinnedMeshRenderer r,out Vector3[] points,out Vector3[] normals,KaiFitSurface surface=null)
        {
            var mesh=new Mesh();
            try
            {
                r.BakeMesh(mesh,UseScale(r));var m=r.transform.localToWorldMatrix;var normal=m.inverse.transpose;
                points=mesh.vertices.Select(m.MultiplyPoint3x4).ToArray();normals=mesh.normals.Select(v=>normal.MultiplyVector(v).normalized).ToArray();
                if(surface==null)return new KaiFitSurface(points,normals,mesh.triangles);
                surface.Refit(points,normals);return surface;
            }
            finally {UnityEngine.Object.DestroyImmediate(mesh);}
        }
        void RefreshWorking(Part p)
        {
            p.Working.vertices=p.Basis.Select((v,i)=>v+p.Offsets[i]).ToArray();p.Working.RecalculateBounds();
            if(p.Offsets.Any(v=>v.sqrMagnitude>1e-14f))
            {p.Working.RecalculateNormals();if(p.Working.uv.Length==p.Working.vertexCount)p.Working.RecalculateTangents();}
            else {p.Working.normals=p.Original.normals;p.Working.tangents=p.Original.tangents;}
            p.Surface=Bake(p.Copy,out p.Points,out p.Normals,p.Surface);
        }
        void RefreshBody()
        {
            Vector3[] points,normals; bodySurface=Bake(bodyCopy,out points,out normals,bodySurface);
            var bounds=new Bounds(points[0],Vector3.zero);foreach(var p in points)bounds.Encapsulate(p);FocusBounds=bounds;
        }
        public void SetValue(float value)
        {
            if(float.IsNaN(value)||float.IsInfinity(value)||value<0||value>100)throw new InvalidOperationException("胸サイズは0〜100で指定してください。");
            Value=value;
            if(!string.IsNullOrEmpty(Shape))bodyCopy.SetBlendShapeWeight(bodyCopy.sharedMesh.GetBlendShapeIndex(Shape),value);
            RefreshBody();
        }
        Vector3[][] Snapshot()=>parts.Select(p=>(Vector3[])p.Offsets.Clone()).ToArray();
        void Restore(Vector3[][] state){for(int i=0;i<parts.Count;i++){parts[i].Offsets=state[i];RefreshWorking(parts[i]);}}
        public void BeginStroke()
        {
            if(undo.Count>=20)
            {var keep=undo.Take(19).Reverse().ToArray();undo.Clear();foreach(var item in keep)undo.Push(item);}
            undo.Push(Snapshot());redo.Clear();
        }
        public void UndoStroke(){if(undo.Count==0)return;redo.Push(Snapshot());Restore(undo.Pop());}
        public void RedoStroke(){if(redo.Count==0)return;undo.Push(Snapshot());Restore(redo.Pop());}
        public void Reset(){BeginStroke();foreach(var p in parts){p.Offsets=new Vector3[p.Basis.Length];RefreshWorking(p);}}
        public int Brush(Vector3 point,Vector3 cameraPosition,float radius,float strength,float gap,KaiFitBrush mode)
        {
            if(radius<=0||radius>.5f||strength<=0||strength>1||gap<0||gap>.02f || new[]{radius,strength,gap}.Any(v=>float.IsNaN(v)||float.IsInfinity(v)))
                throw new InvalidOperationException("ブラシの大きさ・強さ・隙間を確認してください。");
            int count=0;
            var affected=parts.Select(p=>new bool[p.Points.Length]).ToArray();
            foreach(var p in parts)
            {
                int changed=0;
                for(int i=0;i<p.Offsets.Length;i++)
                {
                    float d=Vector3.Distance(point,p.Points[i]);if(d>=radius)continue;
                    var normal=p.BrushNormals.Length==p.Offsets.Length?p.BrushNormals[i]:Vector3.zero;
                    var toVertex=p.Points[i]-cameraPosition;float distance=toVertex.magnitude;
                    if(Vector3.Dot(normal,-toVertex)<=0||distance<.0001f)continue;
                    // Use the starting garment silhouette: moving the front must not
                    // expose and then automatically edit its formerly hidden rear panel.
                    // Other meshes do not occlude it, allowing layered group correction.
                    KaiFitSurface.Hit occluder;
                    var toReference=p.ReferencePoints[i]-cameraPosition;float referenceDistance=toReference.magnitude;
                    if(referenceDistance<.0001f)continue;
                    if(p.ReferenceSurface.Raycast(new Ray(cameraPosition,toReference/referenceDistance),out occluder) && occluder.Distance<referenceDistance-.001f)continue;
                    affected[parts.IndexOf(p)][i]=true;
                    float amount=strength*(1-Mathf.SmoothStep(0,1,d/radius));Vector3 delta;
                    if(mode==KaiFitBrush.Restore)delta=-p.Offsets[i]*amount;
                    else
                    {
                        Vector3 movement;
                        if(mode==KaiFitBrush.Push)movement=normal*(.005f*amount);
                        else
                        {
                            if(!bodySurface.OutwardDistance(p.Points[i],normal,Mathf.Max(radius,.03f),gap,out float needed))continue;
                            movement=normal*Mathf.Min(needed,.02f)*amount;
                        }
                        delta=p.InverseSkin[i].MultiplyVector(movement);
                    }
                    if(delta.sqrMagnitude<1e-16f)continue;
                    p.Offsets[i]+=delta;changed++;
                }
                changed+=p.Seams.Synchronize(p.Offsets,affected[parts.IndexOf(p)],mode==KaiFitBrush.Restore);
                if(changed>0)RefreshWorking(p);count+=changed;
            }
            if(mode==KaiFitBrush.Correct)count+=RepairBodyFaces(affected,cameraPosition,gap,strength);
            if(PreserveLayers&&parts.Count>1&&mode!=KaiFitBrush.Restore)count+=ResolveLayers(affected,cameraPosition);
            return count;
        }
        public int RepairSurfaces(Vector3 cameraPosition,float gap)
        {
            if(gap<0||gap>.01f||float.IsNaN(gap)||float.IsInfinity(gap))throw new InvalidOperationException("肌との隙間は0〜10mmで指定してください。");
            BeginStroke();var affected=parts.Select(p=>p.Offsets.Select(v=>v.sqrMagnitude>1e-14f).ToArray()).ToArray();int count=0;
            PrepareAllowed(cameraPosition);
            foreach(var p in parts){int n=p.Seams.Synchronize(p.Offsets,affected[parts.IndexOf(p)]);if(n>0)RefreshWorking(p);count+=n;}
            foreach(var p in parts)for(int pass=0;pass<4;pass++)
            {
                int k=parts.IndexOf(p);var visible=affected[k].Select((v,i)=>v&&layerAllowed[k][i]).ToArray();
                int n=p.Seams.SmoothOutside(p.Offsets,p.Points,p.InverseSkin,visible,bodySurface,gap);
                n+=p.Seams.Synchronize(p.Offsets,affected[k]);if(n==0)break;RefreshWorking(p);count+=n;
            }
            count+=RepairBodyFaces(affected,cameraPosition,gap,1,3);
            if(PreserveLayers&&parts.Count>1)count+=ResolveLayers(affected,cameraPosition);
            return count;
        }
        public int RepairLayers(Vector3 cameraPosition)
        {
            BeginStroke();
            return ResolveLayers(parts.Select(p=>p.Offsets.Select(v=>v.sqrMagnitude>1e-14f).ToArray()).ToArray(),cameraPosition);
        }
        int ResolveLayers(bool[][] affected,Vector3 cameraPosition)
        {
            int count=0;
            PrepareAllowed(cameraPosition);
            // A seam copy moved by one constraint may touch the next garment.
            for(int pass=0;pass<2;pass++)
            {
                var points=parts.Select(p=>(Vector3[])p.Points.Clone()).ToArray();
                layers.Resolve(points,affected,LayerGap,layerAllowed);
                for(int k=0;k<parts.Count;k++)
                {
                    var p=parts[k];bool changed=false;
                    for(int i=0;i<p.Points.Length;i++)
                    {
                        var movement=points[k][i]-p.Points[i];if(movement.sqrMagnitude<1e-14f)continue;
                        p.Offsets[i]+=p.InverseSkin[i].MultiplyVector(movement);changed=true;count++;
                    }
                    int seams=p.Seams.Synchronize(p.Offsets,affected[k]);count+=seams;
                    if(changed||seams>0)RefreshWorking(p);
                }
            }
            return count;
        }
        void PrepareAllowed(Vector3 cameraPosition)
        {
            if(layerAllowed!=null&&(layerCamera-cameraPosition).sqrMagnitude<=1e-10f)return;
            layerCamera=cameraPosition;
            layerAllowed=parts.Select(p=>p.ReferencePoints.Select((v,i)=>
            {
                var ray=v-cameraPosition;float distance=ray.magnitude;
                if(distance<.0001f||Vector3.Dot(p.BrushNormals[i],-ray)<=0)return false;
                return !p.ReferenceSurface.Raycast(new Ray(cameraPosition,ray/distance),out var hit)||hit.Distance>=distance-.001f;
            }).ToArray()).ToArray();
        }
        int RepairBodyFaces(bool[][] affected,Vector3 cameraPosition,float gap,float strength,int rounds=1)
        {
            PrepareAllowed(cameraPosition);int count=0;
            for(int k=0;k<parts.Count;k++)
            {
                var p=parts[k];var region=(bool[])affected[k].Clone();
                for(int round=0;round<rounds;round++)
                {
                var points=(Vector3[])p.Points.Clone();var mask=(bool[])region.Clone();
                KaiFitCloth.RepairBody(points,p.BrushNormals,p.Working.triangles,mask,layerAllowed[k],bodySurface,gap,strength);
                bool changed=false;
                for(int i=0;i<points.Length;i++)
                {
                    var delta=points[i]-p.Points[i];if(delta.sqrMagnitude<1e-14f)continue;
                    p.Offsets[i]+=p.InverseSkin[i].MultiplyVector(delta);changed=true;count++;
                }
                int seams=p.Seams.Synchronize(p.Offsets,mask);count+=seams;
                for(int i=0;i<mask.Length;i++)affected[k][i]|=mask[i];
                if(changed||seams>0)RefreshWorking(p);
                else break;
                }
            }
            return count;
        }
        public bool Pick(Vector2 position,Rect rect,out KaiFitSurface.Hit hit)
        {
            var ray=preview.camera.ViewportPointToRay(new Vector3((position.x-rect.x)/rect.width,1-(position.y-rect.y)/rect.height,0));
            bool found=bodySurface.Raycast(ray,out hit);
            foreach(var p in parts)
                if(p.Surface.Raycast(ray,out var candidate)&&(!found||candidate.Distance<hit.Distance)){hit=candidate;found=true;}
            return found;
        }
        public Vector3 CameraPosition => preview.camera.transform.position;
        public float ScreenRadius(float radius,Vector3 point,Rect rect)
        {
            float distance=Vector3.Dot(point-preview.camera.transform.position,preview.camera.transform.forward);
            return radius/Mathf.Max(.001f,2*distance*Mathf.Tan(preview.camera.fieldOfView*.5f*Mathf.Deg2Rad))*rect.height;
        }
        public void Draw(Rect rect,bool before,bool closeup,float yaw,float pitch,float zoom)
        {
            if(Event.current.type!=EventType.Repaint)return;
            var bounds=FocusBounds;
            if(closeup)
            {
                var animator=clone.GetComponent<Animator>();var chest=animator!=null&&animator.isHuman?animator.GetBoneTransform(HumanBodyBones.Chest):null;
                float height=Mathf.Max(.1f,bounds.size.y*.33f);
                bounds=new Bounds(chest!=null?chest.position:bounds.center+Vector3.up*bounds.size.y*.2f,Vector3.one*height);
            }
            float distance=Mathf.Max(.01f,bounds.extents.magnitude)/Mathf.Sin(15*Mathf.Deg2Rad)/Mathf.Clamp(zoom,.5f,3);
            var camera=preview.camera;camera.fieldOfView=30;camera.nearClipPlane=.005f;camera.farClipPlane=distance+FocusBounds.size.magnitude*3+10;
            camera.transform.position=bounds.center+Quaternion.Euler(pitch,yaw,0)*Vector3.forward*distance;camera.transform.LookAt(bounds.center);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.14f,.15f,.17f,1);
            preview.ambientColor=new Color(.7f,.7f,.7f,1);preview.lights[0].intensity=1.2f;preview.lights[0].transform.rotation=Quaternion.Euler(40,35,0);preview.lights[1].intensity=.8f;
            foreach(var p in parts)p.Copy.sharedMesh=before?p.Original:p.Working;
            try
            {
                preview.BeginPreview(rect,GUIStyle.none);
                try {preview.Render();}finally {var image=preview.EndPreview();GUI.DrawTexture(rect,image,ScaleMode.StretchToFill,false);}
            }
            finally {foreach(var p in parts)p.Copy.sharedMesh=p.Working;}
        }
        Part GetPart(SkinnedMeshRenderer renderer)=>parts.First(p=>p.Source==renderer);
        public Mesh OriginalFor(SkinnedMeshRenderer renderer)=>GetPart(renderer).Original;
        public Mesh CreateResult()=>CreateResult(Clothing);
        public Mesh CreateResult(SkinnedMeshRenderer renderer)
        {
            RequireUnchangedSource();var p=GetPart(renderer);var mesh=UnityEngine.Object.Instantiate(p.Working);
            mesh.hideFlags=HideFlags.None;mesh.name=p.Original.name+"_胸衣装補正";return mesh;
        }
        public Bounds ResultBounds()=>ResultBounds(Clothing);
        public Bounds ResultBounds(SkinnedMeshRenderer renderer)
        {
            var bounds=renderer.localBounds;foreach(var p in GetPart(renderer).Points)bounds.Encapsulate(renderer.transform.InverseTransformPoint(p));return bounds;
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            if(preview!=null)preview.Cleanup();if(clone!=null)UnityEngine.Object.DestroyImmediate(clone);foreach(var p in parts)if(p.Working!=null)UnityEngine.Object.DestroyImmediate(p.Working);
        }
    }
}
#endif
