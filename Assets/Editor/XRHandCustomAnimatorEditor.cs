using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

#if UNITY_EDITOR
[CustomEditor(typeof(XRHandCustomAnimator))]
public class XRHandCustomAnimatorEditor : Editor
{
    private class JointTransformReferenceReorderableList : ReorderableList
    {
        public JointTransformReferenceReorderableList(SerializedObject serializedObject, SerializedProperty serializedProperty)
            : base(serializedObject, serializedProperty, true, true, true, true)
        {
            drawElementCallback = OnDrawListElement;
            drawHeaderCallback = OnDrawListHeader;
            elementHeight = EditorGUIUtility.singleLineHeight;
        }
        void OnDrawListHeader(Rect rect)
        {
            GUI.Label(rect, "JointTransformReferences");
        }
        void OnDrawListElement(Rect rect, int elementIndex, bool isActive, bool isFocused)
        {
            var element = serializedProperty.GetArrayElementAtIndex(elementIndex);
            rect.yMin += 2;
            var width = rect.width;
            var middle = rect.xMin + width * 0.5f;
            const float padding = 8f;
            rect.xMax = middle - padding;
            EditorGUI.PropertyField(rect, element.FindPropertyRelative("m_XRHandJointID"), GUIContent.none);
            rect.xMax = rect.xMin + width;
            rect.xMin = middle;
            EditorGUI.PropertyField(rect, element.FindPropertyRelative("m_JointTransform"), GUIContent.none);
        }
    }
    
    private static readonly GUIContent sJointTransformReferences = EditorGUIUtility.TrTextContent("Joint Transform References");
    private static readonly GUIContent sRootTransform = EditorGUIUtility.TrTextContent("ParentTransform");
    private const string mJointsExpandedStateKey = "XRHands." + nameof(XRHandCustomAnimator) + ".JointsExpanded";
    private JointTransformReferenceReorderableList mJointTransformReferencesList;
    private SerializedProperty mController;
    private SerializedProperty mMeshRenderer;
    private SerializedProperty mJointReferences;
    private SerializedProperty mRootTransform;
    private SerializedProperty mAnimator;
    private SerializedProperty mAnimationSpeed;
    private SerializedProperty mTriggerAction;
    private SerializedProperty mGripAction;
    private SerializedProperty mPrimaryAction;
    private SerializedProperty mPrimaryClickAction;
    private SerializedProperty mSecondaryAction;
    private SerializedProperty mSecondaryClickAction;
    private SerializedProperty mThumbStickAction;
    private SerializedProperty mUserControllerOffset;
    private SerializedProperty mControllerPositionOffset;
    private SerializedProperty mControllerRotationOffset;
    private SerializedProperty mGripFingerFlag;
    private SerializedProperty mTriggerFingerFlag;
    private SerializedProperty mThumbFingerFlag;

    private bool mJointsExpanded;
    private bool mDrawHelpBoxForFindingJoints;
    private string mErrorFindingJointsMessage;
    private readonly List<string> mMissingJointNames = new List<string>();
    
    protected virtual void OnEnable()
    {
        mController = serializedObject.FindProperty("TrackingEventsTransform");
        mMeshRenderer = serializedObject.FindProperty("HandMeshRenderer");
        mJointReferences = serializedObject.FindProperty("JointTransformReferences");
        mRootTransform = serializedObject.FindProperty("RootTransform");
        mAnimator = serializedObject.FindProperty("HandAnimator");
        mAnimationSpeed = serializedObject.FindProperty("AnimationSpeed");
        mTriggerAction = serializedObject.FindProperty("TriggerPressAction");
        mGripAction = serializedObject.FindProperty("GripPressAction");
        mPrimaryAction = serializedObject.FindProperty("PrimaryTouchAction");
        mPrimaryClickAction = serializedObject.FindProperty("PrimaryClickAction");
        mSecondaryAction = serializedObject.FindProperty("SecondaryTouchAction");
        mSecondaryClickAction = serializedObject.FindProperty("SecondaryClickAction");
        mThumbStickAction = serializedObject.FindProperty("ThumbStickTouchAction");
        mUserControllerOffset = serializedObject.FindProperty("UseControllerOffset");
        mControllerPositionOffset = serializedObject.FindProperty("PositionOffset");
        mControllerRotationOffset = serializedObject.FindProperty("RotationOffset");
        mGripFingerFlag = serializedObject.FindProperty("GripFingerTypes");
        mTriggerFingerFlag = serializedObject.FindProperty("TriggerFingerTypes");
        mThumbFingerFlag = serializedObject.FindProperty("ThumbFingerTypes");
        mJointTransformReferencesList = new JointTransformReferenceReorderableList(serializedObject, mJointReferences);
    }
    private void OnDisable()
    {
        SessionState.SetBool(mJointsExpandedStateKey,mJointsExpanded);
    }
    
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField(EditorGUIUtility.TrTempContent("Script"), MonoScript.FromMonoBehaviour(target as MonoBehaviour), typeof(MonoBehaviour), false);
        }
        DrawInspector();
        serializedObject.ApplyModifiedProperties();
    }

    protected virtual void DrawInspector()
    {
        EditorGUILayout.PropertyField(mMeshRenderer);
        EditorGUILayout.PropertyField(mController);
        mJointsExpanded = EditorGUILayout.Foldout(mJointsExpanded, sJointTransformReferences, true);
        if (mJointsExpanded)
        {
            using (new EditorGUI.IndentLevelScope())
            {
                DrawJointTransformsFoldout();
            }
        }
        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(mUserControllerOffset);
        if (mUserControllerOffset.boolValue)
        {
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(mControllerPositionOffset);
                EditorGUILayout.PropertyField(mControllerRotationOffset);
            }
        }
        EditorGUILayout.PropertyField(mAnimator);
        EditorGUILayout.PropertyField(mAnimationSpeed);
        EditorGUILayout.PropertyField(mTriggerAction);
        EditorGUILayout.PropertyField(mGripAction);
        EditorGUILayout.PropertyField(mPrimaryAction);
        EditorGUILayout.PropertyField(mPrimaryClickAction);
        EditorGUILayout.PropertyField(mSecondaryAction);
        EditorGUILayout.PropertyField(mSecondaryClickAction);
        EditorGUILayout.PropertyField(mThumbStickAction);
        EditorGUILayout.PropertyField(mGripFingerFlag);
        EditorGUILayout.PropertyField(mTriggerFingerFlag);
        EditorGUILayout.PropertyField(mThumbFingerFlag);
    }

    private void DrawJointTransformsFoldout()
    {
        EditorGUILayout.PropertyField(mRootTransform,sRootTransform);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(mRootTransform.objectReferenceValue == null))
            {
                if (GUILayout.Button("FindJoints"))
                {
                    OnFindJointsButtonPressed();
                }
            }
            using (new EditorGUI.DisabledScope(mJointReferences.arraySize == 0))
            {
                if (GUILayout.Button("ClearJoints"))
                {
                    mJointReferences.ClearArray();
                }
            }
        }
        
        if (mDrawHelpBoxForFindingJoints)
        {
            EditorGUILayout.HelpBox(mErrorFindingJointsMessage, MessageType.Warning, true);
        }
        mJointTransformReferencesList.DoLayoutList();
    }
    
    private void OnFindJointsButtonPressed()
    {
        foreach (var targetObject in targets)
        {
            var customAnimator = (XRHandCustomAnimator)targetObject;
            Undo.RecordObject(customAnimator, "Find Joints from Parent");
            customAnimator.FindJointsFromRoot(mMissingJointNames);
            EditorUtility.SetDirty(customAnimator);
            if (mMissingJointNames.Count > 0)
            {
                mDrawHelpBoxForFindingJoints = true;
                mErrorFindingJointsMessage = $"Could not find joints: {string.Join(", ", mMissingJointNames)}";
            }
            else
            {
                mDrawHelpBoxForFindingJoints = false;
            }
        }
        serializedObject.SetIsDifferentCacheDirty();
    }
    
}
#endif

