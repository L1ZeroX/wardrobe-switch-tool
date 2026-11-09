#if RISV4_JSON
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace YagihataItems.RadialInventorySystemV4
{
    public static class KaiChestFitStore
    {
        public static string Apply(KaiChestFitSession session)
        {
            session.RequireUnchangedSource();var targets=session.Targets;
            if(EditorApplication.isPlayingOrWillChangePlaymode || targets.Any(EditorUtility.IsPersistent))throw new InvalidOperationException("シーン上のアバターを、再生停止中に編集してください。");
            var old=targets.Select(r=>r.GetComponent<KaiChestFitRecord>()).ToArray();
            foreach(var record in old)
                if(record!=null && (record.Body!=session.Body || record.BodyShape!=session.Shape))throw new InvalidOperationException("補正元を変更する場合は、先に復元タブで衣装を元に戻し、補正記録を解除してください。");
            if(!string.IsNullOrEmpty(session.Shape) && session.Root.GetComponentsInChildren<KaiChestFitRecord>(true).Any(r=>!old.Contains(r)&&r.Applied&&r.Body==session.Body&&r.BodyShape==session.Shape&&Mathf.Abs(r.AppliedBodyValue-session.Value)>.001f))
                throw new InvalidOperationException("対象外の衣装に異なる胸サイズの補正が適用されています。先に復元タブでその補正をOFFにしてください。");
            var origin=session.Root.GetComponentsInChildren<KaiChestFitRecord>(true).FirstOrDefault(r=>r.Applied&&r.Body==session.Body&&r.BodyShape==session.Shape);
            float originalValue=origin!=null?origin.OriginalBodyValue:session.InitialValue;
            var meshes=new System.Collections.Generic.List<Mesh>();var paths=new System.Collections.Generic.List<string>();int group=-1;
            try
            {
                // Prepare every result before assigning anything: all targets share one body.
                foreach(var target in targets)meshes.Add(session.CreateResult(target));
                string folder=RIS.WorkFolderPath+"KaiChestFits";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
                foreach(var mesh in meshes)
                {
                    string path=AssetDatabase.GenerateUniqueAssetPath(folder+"/Fit_"+Guid.NewGuid().ToString("N")+".asset");
                    AssetDatabase.CreateAsset(mesh,path);paths.Add(path);
                }
                session.RequireUnchangedSource();
                Undo.IncrementCurrentGroup();group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("衣装グループの胸補正を適用");
                string batch=Guid.NewGuid().ToString("N");
                for(int i=0;i<targets.Length;i++)
                {
                    var target=targets[i];var record=old[i] ?? Undo.AddComponent<KaiChestFitRecord>(target.gameObject);
                    Undo.RecordObjects(new UnityEngine.Object[]{record,target,session.Body},"衣装グループの胸補正を適用");
                    if(old[i]==null)
                    {record.OriginalMesh=session.OriginalFor(target);record.OriginalBounds=target.localBounds;record.OriginalBodyValue=originalValue;}
                    record.Clothing=target;record.Body=session.Body;record.BodyShape=session.Shape;record.BatchID=batch;
                    record.CorrectedMesh=meshes[i];record.CorrectedBounds=session.ResultBounds(target);record.AppliedBodyValue=session.Value;record.Applied=true;
                    target.sharedMesh=meshes[i];target.localBounds=record.CorrectedBounds;SetBody(record,record.AppliedBodyValue);Dirty(record);
                }
                Undo.FlushUndoRecordObjects();Undo.CollapseUndoOperations(group);AssetDatabase.SaveAssets();return paths[0];
            }
            catch
            {
                if(group>=0)Undo.RevertAllDownToGroup(group);
                foreach(var mesh in meshes)if(mesh!=null&&!AssetDatabase.Contains(mesh))UnityEngine.Object.DestroyImmediate(mesh);
                throw;
            }
        }
        static GameObject Root(KaiChestFitRecord r)
        {
            var descriptor=r.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            return descriptor!=null?descriptor.gameObject:r.transform.root.gameObject;
        }
        static void CheckOtherFits(GameObject root,KaiChestFitRecord self,SkinnedMeshRenderer body,string shape,float value)
        {
            if(string.IsNullOrEmpty(shape))return;
            if(root.GetComponentsInChildren<KaiChestFitRecord>(true).Any(r=>r!=self&&r.Applied&&r.Body==body&&r.BodyShape==shape&&Mathf.Abs(r.AppliedBodyValue-value)>.001f))
                throw new InvalidOperationException("別の衣装に異なる胸サイズの補正が適用されています。先に復元タブでその補正をOFFにしてください。");
        }
        static void SetBody(KaiChestFitRecord record,float value)
        {
            if(string.IsNullOrEmpty(record.BodyShape))return;
            int index=record.Body!=null&&record.Body.sharedMesh!=null?record.Body.sharedMesh.GetBlendShapeIndex(record.BodyShape):-1;
            if(index<0)throw new InvalidOperationException("素体のシェイプキーが見つかりません。");
            record.Body.SetBlendShapeWeight(index,value);
        }
        static void Dirty(KaiChestFitRecord r)
        {
            foreach(var obj in new UnityEngine.Object[]{r,r.Clothing,r.Body})if(obj!=null){EditorUtility.SetDirty(obj);PrefabUtility.RecordPrefabInstancePropertyModifications(obj);}
            EditorSceneManager.MarkSceneDirty(r.gameObject.scene);
        }
        public static string SetEnabled(KaiChestFitRecord record,bool enabled)=>SetEnabled(new[]{record},enabled);
        public static string SetEnabled(System.Collections.Generic.IEnumerable<KaiChestFitRecord> source,bool enabled)
        {
            var records=source.Distinct().ToArray();
            if(records.Length==0)throw new InvalidOperationException("復元する補正がありません。");
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("再生を停止してください。");
            foreach(var r in records)
            {
                if(r==null||r.Clothing==null||r.OriginalMesh==null||r.CorrectedMesh==null)throw new InvalidOperationException("復元用のメッシュまたは衣装が見つかりません。");
                if(r.Clothing.sharedMesh!=r.OriginalMesh&&r.Clothing.sharedMesh!=r.CorrectedMesh)
                    throw new InvalidOperationException(r.Clothing.name+"：別のツールでメッシュが変更されています。すべての変更を中止しました。");
                if(enabled&&!string.IsNullOrEmpty(r.BodyShape))
                {
                    if(r.Body==null||r.Body.sharedMesh==null||r.Body.sharedMesh.GetBlendShapeIndex(r.BodyShape)<0)throw new InvalidOperationException("素体のシェイプキーが見つかりません。");
                    if(Root(r).GetComponentsInChildren<KaiChestFitRecord>(true).Any(x=>x!=r&&(records.Contains(x)||x.Applied)&&x.Body==r.Body&&x.BodyShape==r.BodyShape&&Mathf.Abs(x.AppliedBodyValue-r.AppliedBodyValue)>.001f))
                        throw new InvalidOperationException("異なる胸サイズの補正が含まれています。同じサイズの衣装を選んでください。");
                }
            }
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();string note="";
            try
            {
                Undo.RecordObjects(records.SelectMany(r=>new UnityEngine.Object[]{r,r.Clothing,r.Body}).Where(o=>o!=null).Distinct().ToArray(),enabled?"衣装補正をON":"衣装補正をOFF");
                foreach(var r in records)
                {
                    r.Clothing.sharedMesh=enabled?r.CorrectedMesh:r.OriginalMesh;r.Clothing.localBounds=enabled?r.CorrectedBounds:r.OriginalBounds;
                    if(enabled)SetBody(r,r.AppliedBodyValue);
                    else if(r.Applied&&!string.IsNullOrEmpty(r.BodyShape)&&r.Body!=null&&r.Body.sharedMesh!=null)
                    {
                        int index=r.Body.sharedMesh.GetBlendShapeIndex(r.BodyShape);
                        bool other=Root(r).GetComponentsInChildren<KaiChestFitRecord>(true).Any(x=>!records.Contains(x)&&x.Applied&&x.Body==r.Body&&x.BodyShape==r.BodyShape);
                        if(index>=0&&!other&&Mathf.Abs(r.Body.GetBlendShapeWeight(index)-r.AppliedBodyValue)<.001f)SetBody(r,r.OriginalBodyValue);
                        else note=" 素体の値は他の補正・編集を保持しました。";
                    }
                    r.Applied=enabled;Dirty(r);
                }
                Undo.FlushUndoRecordObjects();Undo.CollapseUndoOperations(group);
                return records.Length+"メッシュの補正を"+(enabled?"ONにしました。":"OFFにして元へ戻しました。")+note;
            }
            catch {Undo.RevertAllDownToGroup(group);throw;}
        }
        public static void ForgetDisabled(KaiChestFitRecord r)
        {
            if(r==null||r.Applied||r.Clothing==null||r.Clothing.sharedMesh!=r.OriginalMesh)throw new InvalidOperationException("先に補正をOFFにしてください。");
            Undo.DestroyObjectImmediate(r); // Generated assets are deliberately kept for Undo/recovery.
        }
    }
    [CustomEditor(typeof(KaiChestFitRecord))]
    public sealed class KaiChestFitRecordEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var r=(KaiChestFitRecord)target;
            EditorGUILayout.HelpBox("胸・衣装補正の復元記録です。元のメッシュは保持されています。この記録はアップロード時に除外されます。",MessageType.Info);
            using(new EditorGUI.DisabledScope(true))
            {EditorGUILayout.ObjectField("元メッシュ",r.OriginalMesh,typeof(Mesh),false);EditorGUILayout.ObjectField("補正メッシュ",r.CorrectedMesh,typeof(Mesh),false);}
            if(GUILayout.Button("調整・復元画面を開く"))KaiChestFitWindow.Open(r.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>()?.gameObject);
        }
    }
}
#endif
