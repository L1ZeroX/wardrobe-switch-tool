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
        public readonly GameObject Root;
        public readonly SkinnedMeshRenderer Body, Clothing;
        public readonly Mesh Original;
        public readonly string Shape;
        public readonly float InitialValue;
        public float Value { get; private set; }
        public Bounds FocusBounds { get; private set; }
        public Vector3[] Offsets { get; private set; }
        public bool Changed => Offsets.Where((v,i)=>(v-initialOffsets[i]).sqrMagnitude>1e-14f).Any() || Mathf.Abs(Value-InitialValue)>.0001f;
        public int ChangedVertices => Offsets.Count(v=>v.sqrMagnitude>1e-14f);
        public int UndoCount => undo.Count;
        readonly Vector3[] initialOffsets, basis;
        readonly Stack<Vector3[]> undo=new Stack<Vector3[]>(), redo=new Stack<Vector3[]>();
        readonly string sourceStamp;
        PreviewRenderUtility preview;
        GameObject clone;
        SkinnedMeshRenderer bodyCopy, clothCopy;
        Mesh working;
        KaiFitSurface bodySurface, clothSurface;
        Vector3[] clothPoints, clothNormals;
        Matrix4x4[] inverseSkin;
        float[] sourceWeights;
        bool disposed;

        public KaiChestFitSession(GameObject root,SkinnedMeshRenderer body,SkinnedMeshRenderer clothing,string shape)
        {
            Root=root; Body=body; Clothing=clothing; Shape=shape;
            if(root==null||body==null||clothing==null||body==clothing || !body.transform.IsChildOf(root.transform)||!clothing.transform.IsChildOf(root.transform))
                throw new InvalidOperationException("同じアバター内の素体と衣装を別々に選んでください。");
            if(body.sharedMesh==null||clothing.sharedMesh==null)throw new InvalidOperationException("メッシュがありません。");
            int si=string.IsNullOrEmpty(shape)?-1:body.sharedMesh.GetBlendShapeIndex(shape);
            if(!string.IsNullOrEmpty(shape)&&si<0)throw new InvalidOperationException("胸のシェイプキーを選び直してください。");
            InitialValue=Value=si<0?0:body.GetBlendShapeWeight(si);
            var record=clothing.GetComponent<KaiChestFitRecord>();
            if(record!=null && clothing.sharedMesh!=record.OriginalMesh && clothing.sharedMesh!=record.CorrectedMesh)
                throw new InvalidOperationException("補正後に別のツールでメッシュが変更されています。復元タブで状態を確認してください。");
            Original=record!=null?record.OriginalMesh:clothing.sharedMesh;
            if(Original==null)throw new InvalidOperationException("復元元メッシュが見つかりません。");
            basis=Original.vertices;
            var current=clothing.sharedMesh.vertices;
            if(current.Length!=basis.Length)throw new InvalidOperationException("元と現在の頂点数が異なります。");
            Offsets=current.Select((v,i)=>v-basis[i]).ToArray(); initialOffsets=(Vector3[])Offsets.Clone();
            sourceWeights=Enumerable.Range(0,clothing.sharedMesh.blendShapeCount).Select(clothing.GetBlendShapeWeight).ToArray();
            sourceStamp=Stamp();
            try
            {
                preview=new PreviewRenderUtility(); clone=UnityEngine.Object.Instantiate(root); clone.hideFlags=HideFlags.HideAndDontSave; preview.AddSingleGO(clone);
                foreach(var b in clone.GetComponentsInChildren<Behaviour>(true))if(b!=null)b.enabled=false;
                foreach(var r in clone.GetComponentsInChildren<Renderer>(true))r.enabled=false;
                bodyCopy=Find(body); clothCopy=Find(clothing);
                foreach(var r in new[]{bodyCopy,clothCopy})
                {
                    r.enabled=true; r.updateWhenOffscreen=true;
                    for(var t=r.transform;t!=null && t.IsChildOf(clone.transform);t=t.parent)t.gameObject.SetActive(true);
                }
                working=UnityEngine.Object.Instantiate(Original); working.hideFlags=HideFlags.HideAndDontSave; clothCopy.sharedMesh=working;
                for(int i=0;i<sourceWeights.Length;i++)clothCopy.SetBlendShapeWeight(i,sourceWeights[i]);
                inverseSkin=InverseSkinMatrices(clothCopy);
                RefreshWorking(); RefreshBody();
            }
            catch {Dispose();throw;}
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
            if(Body==null||Clothing==null||Body.sharedMesh==null||Clothing.sharedMesh==null)return "missing";
            return string.Join("|",new[]{Body,Clothing}.Select(r=>
                r.sharedMesh.GetInstanceID()+":"+AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(r.sharedMesh))+":"+
                r.transform.localToWorldMatrix.ToString("R")+":"+
                string.Join(",",Enumerable.Range(0,r.sharedMesh.blendShapeCount).Select(i=>r.GetBlendShapeWeight(i).ToString("R")))+":"+
                string.Join(";",r.bones.Select(b=>b==null?"null":b.localToWorldMatrix.ToString("R")))));
        }
        public void RequireUnchangedSource()
        {
            if(disposed||Root==null||Body==null||Clothing==null||Stamp()!=sourceStamp)
                throw new InvalidOperationException("編集中に元アバターが変更されました。画面の対象を選び直し、最新状態から補正してください。");
        }
        static Matrix4x4[] InverseSkinMatrices(SkinnedMeshRenderer r)
        {
            var mesh=r.sharedMesh; var result=new Matrix4x4[mesh.vertexCount];
            var counts=mesh.GetBonesPerVertex();var weights=mesh.GetAllBoneWeights();
            try
            {
                var binds=mesh.bindposes;var bones=r.bones;int at=0;
                for(int i=0;i<result.Length;i++)
                {
                    int count=counts.Length==result.Length?counts[i]:0;var sum=new Matrix4x4();float total=0;
                    for(int b=0;b<count;b++)
                    {
                        var w=weights[at++]; if(w.weight<=0)continue;
                        if(w.boneIndex>=bones.Length||w.boneIndex>=binds.Length||bones[w.boneIndex]==null)throw new InvalidOperationException("衣装のボーン参照が不足しています。");
                        var m=bones[w.boneIndex].localToWorldMatrix*binds[w.boneIndex];
                        for(int k=0;k<16;k++)sum[k]+=m[k]*w.weight; total+=w.weight;
                    }
                    if(total==0)sum=r.transform.localToWorldMatrix;
                    if(Mathf.Abs(sum.determinant)<1e-10f)throw new InvalidOperationException("ゼロスケールなどで変形を戻せないボーンがあります。");
                    result[i]=sum.inverse;
                }
                return result;
            }
            finally {counts.Dispose();weights.Dispose();}
        }
        static KaiFitSurface Bake(SkinnedMeshRenderer r,out Vector3[] points,out Vector3[] normals)
        {
            var mesh=new Mesh();
            try
            {
                // With skinning, true returns renderer-local vertices including the
                // inverse renderer scale. false would apply bone/world scale twice below.
                r.BakeMesh(mesh,r.bones.Length>0 && r.sharedMesh.bindposes.Length>0);
                var m=r.transform.localToWorldMatrix;var normal=m.inverse.transpose;
                points=mesh.vertices.Select(m.MultiplyPoint3x4).ToArray();normals=mesh.normals.Select(v=>normal.MultiplyVector(v).normalized).ToArray();
                return new KaiFitSurface(points,normals,mesh.triangles);
            }
            finally {UnityEngine.Object.DestroyImmediate(mesh);}
        }
        void RefreshWorking()
        {
            working.vertices=basis.Select((v,i)=>v+Offsets[i]).ToArray();working.RecalculateBounds();
            if(Offsets.Any(v=>v.sqrMagnitude>1e-14f))
            {working.RecalculateNormals();if(working.uv.Length==working.vertexCount)working.RecalculateTangents();}
            else {working.normals=Original.normals;working.tangents=Original.tangents;}
            clothSurface=Bake(clothCopy,out clothPoints,out clothNormals);
        }
        void RefreshBody()
        {
            Vector3[] points,normals; bodySurface=Bake(bodyCopy,out points,out normals);
            var bounds=new Bounds(points[0],Vector3.zero);foreach(var p in points)bounds.Encapsulate(p);FocusBounds=bounds;
        }
        public void SetValue(float value)
        {
            if(float.IsNaN(value)||float.IsInfinity(value)||value<0||value>100)throw new InvalidOperationException("胸サイズは0〜100で指定してください。");
            Value=value;
            if(!string.IsNullOrEmpty(Shape))bodyCopy.SetBlendShapeWeight(bodyCopy.sharedMesh.GetBlendShapeIndex(Shape),value);
            RefreshBody();
        }
        public void BeginStroke()
        {
            if(undo.Count>=20)
            {
                var keep=undo.Take(19).Reverse().ToArray();undo.Clear();foreach(var item in keep)undo.Push(item);
            }
            undo.Push((Vector3[])Offsets.Clone());redo.Clear();
        }
        public void UndoStroke() {if(undo.Count==0)return;redo.Push((Vector3[])Offsets.Clone());Offsets=undo.Pop();RefreshWorking();}
        public void RedoStroke() {if(redo.Count==0)return;undo.Push((Vector3[])Offsets.Clone());Offsets=redo.Pop();RefreshWorking();}
        public void Reset() {BeginStroke();Offsets=new Vector3[basis.Length];RefreshWorking();}
        public int Brush(Vector3 point,Vector3 cameraPosition,float radius,float strength,float gap,KaiFitBrush mode)
        {
            if(radius<=0||radius>.5f||strength<=0||strength>1||gap<0||gap>.02f || new[]{radius,strength,gap}.Any(v=>float.IsNaN(v)||float.IsInfinity(v)))
                throw new InvalidOperationException("ブラシの大きさ・強さ・隙間を確認してください。");
            int count=0;
            for(int i=0;i<Offsets.Length;i++)
            {
                float d=Vector3.Distance(point,clothPoints[i]);if(d>=radius)continue;
                var normal=clothNormals.Length==Offsets.Length?clothNormals[i]:Vector3.zero;
                if(Vector3.Dot(normal,cameraPosition-clothPoints[i])<=0)continue;
                float falloff=1-Mathf.SmoothStep(0,1,d/radius);float amount=strength*falloff;
                Vector3 delta;
                if(mode==KaiFitBrush.Restore)delta=-Offsets[i]*amount;
                else
                {
                    Vector3 movement;
                    if(mode==KaiFitBrush.Push)movement=normal*(.005f*amount);
                    else
                    {
                        KaiFitSurface.Hit hit;
                        if(!bodySurface.Nearest(clothPoints[i],Mathf.Max(radius,.03f),out hit))continue;
                        float separation=Vector3.Dot(clothPoints[i]-hit.Point,hit.Normal);
                        if(separation>=gap)continue;
                        movement=hit.Normal*Mathf.Min(gap-separation,.02f)*amount;
                    }
                    delta=inverseSkin[i].MultiplyVector(movement);
                }
                if(delta.sqrMagnitude<1e-16f)continue;
                Offsets[i]+=delta;count++;
            }
            if(count>0)RefreshWorking();return count;
        }
        public bool Pick(Vector2 position,Rect rect,out KaiFitSurface.Hit hit)
        {
            var ray=preview.camera.ViewportPointToRay(new Vector3((position.x-rect.x)/rect.width,1-(position.y-rect.y)/rect.height,0));
            KaiFitSurface.Hit clothing,body;bool a=clothSurface.Raycast(ray,out clothing),b=bodySurface.Raycast(ray,out body);
            hit=a && (!b||clothing.Distance<=body.Distance)?clothing:body;return a||b;
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
            clothCopy.sharedMesh=before?Original:working;
            try
            {
                preview.BeginPreview(rect,GUIStyle.none);
                try {preview.Render();}finally {var image=preview.EndPreview();GUI.DrawTexture(rect,image,ScaleMode.StretchToFill,false);}
            }
            finally {clothCopy.sharedMesh=working;}
        }
        public Mesh CreateResult()
        {
            RequireUnchangedSource();var mesh=UnityEngine.Object.Instantiate(working);mesh.hideFlags=HideFlags.None;mesh.name=Original.name+"_胸衣装補正";return mesh;
        }
        public Bounds ResultBounds()
        {
            var bounds=Clothing.localBounds;foreach(var p in clothPoints)bounds.Encapsulate(Clothing.transform.InverseTransformPoint(p));return bounds;
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            if(preview!=null)preview.Cleanup();if(clone!=null)UnityEngine.Object.DestroyImmediate(clone);if(working!=null)UnityEngine.Object.DestroyImmediate(working);
        }
    }
}
#endif
