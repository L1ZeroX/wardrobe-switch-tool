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
            session.RequireUnchangedSource();
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.IsPersistent(session.Clothing))throw new InvalidOperationException("シーン上のアバターを、再生停止中に編集してください。");
            var old=session.Clothing.GetComponent<KaiChestFitRecord>();
            if(old!=null && (old.Body!=session.Body || old.BodyShape!=session.Shape))throw new InvalidOperationException("補正元を変更する場合は、先に復元タブでこの衣装を元に戻してください。");
            CheckOtherFits(session.Root,old,session.Body,session.Shape,session.Value);
            var mesh=session.CreateResult(); string path=null;int group=-1;
            try
            {
                string folder=RIS.WorkFolderPath+"KaiChestFits";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
                path=AssetDatabase.GenerateUniqueAssetPath(folder+"/Fit_"+Guid.NewGuid().ToString("N")+".asset");AssetDatabase.CreateAsset(mesh,path);
                Undo.IncrementCurrentGroup();group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("胸・衣装補正を適用");
                var record=old ?? Undo.AddComponent<KaiChestFitRecord>(session.Clothing.gameObject);
                Undo.RecordObjects(new UnityEngine.Object[]{record,session.Clothing,session.Body},"胸・衣装補正を適用");
                if(old==null)
                {
                    record.OriginalMesh=session.Original;record.OriginalBounds=session.Clothing.localBounds;
                    var other=session.Root.GetComponentsInChildren<KaiChestFitRecord>(true).FirstOrDefault(r=>r!=record&&r.Applied&&r.Body==session.Body&&r.BodyShape==session.Shape);
                    record.OriginalBodyValue=other!=null?other.OriginalBodyValue:session.InitialValue;
                }
                record.Clothing=session.Clothing;record.Body=session.Body;record.BodyShape=session.Shape;
                record.CorrectedMesh=mesh;record.CorrectedBounds=session.ResultBounds();record.AppliedBodyValue=session.Value;record.Applied=true;
                session.Clothing.sharedMesh=mesh;session.Clothing.localBounds=record.CorrectedBounds;
                SetBody(record,record.AppliedBodyValue);Dirty(record);
                Undo.FlushUndoRecordObjects();Undo.CollapseUndoOperations(group);AssetDatabase.SaveAssets();
                return path;
            }
            catch
            {
                if(group>=0)Undo.RevertAllDownToGroup(group);
                // Retain an already created asset for recovery; only temporary objects are destroyed.
                if(mesh!=null && !AssetDatabase.Contains(mesh))UnityEngine.Object.DestroyImmediate(mesh);
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
        public static string SetEnabled(KaiChestFitRecord r,bool enabled)
        {
            if(r==null||r.Clothing==null||r.OriginalMesh==null||r.CorrectedMesh==null)throw new InvalidOperationException("復元用のメッシュまたは衣装が見つかりません。");
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("再生を停止してください。");
            if(r.Clothing.sharedMesh!=r.OriginalMesh&&r.Clothing.sharedMesh!=r.CorrectedMesh)
                throw new InvalidOperationException("別のツールでメッシュが変更されています。上書きを避けるため停止しました。元メッシュは補正記録から確認できます。");
            if(enabled)CheckOtherFits(Root(r),r,r.Body,r.BodyShape,r.AppliedBodyValue);
            if(enabled && !string.IsNullOrEmpty(r.BodyShape) && (r.Body==null||r.Body.sharedMesh==null||r.Body.sharedMesh.GetBlendShapeIndex(r.BodyShape)<0))throw new InvalidOperationException("素体のシェイプキーが見つかりません。");
            Undo.RecordObjects(new UnityEngine.Object[]{r,r.Clothing}.Concat(r.Body!=null?new UnityEngine.Object[]{r.Body}:new UnityEngine.Object[0]).ToArray(),enabled?"衣装補正をON":"衣装補正をOFF");
            r.Clothing.sharedMesh=enabled?r.CorrectedMesh:r.OriginalMesh;r.Clothing.localBounds=enabled?r.CorrectedBounds:r.OriginalBounds;
            string note="";
            if(enabled)SetBody(r,r.AppliedBodyValue);
            else if(!string.IsNullOrEmpty(r.BodyShape) && r.Body!=null && r.Body.sharedMesh!=null)
            {
                int index=r.Body.sharedMesh.GetBlendShapeIndex(r.BodyShape);
                bool other=Root(r).GetComponentsInChildren<KaiChestFitRecord>(true).Any(x=>x!=r&&x.Applied&&x.Body==r.Body&&x.BodyShape==r.BodyShape);
                if(index>=0 && !other && Mathf.Abs(r.Body.GetBlendShapeWeight(index)-r.AppliedBodyValue)<.001f)SetBody(r,r.OriginalBodyValue);
                else note=" 素体の値は他の補正・編集を保持しました。";
            }
            r.Applied=enabled;Dirty(r);return (enabled?"補正をONにしました。":"元の衣装メッシュに戻しました。")+note;
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
