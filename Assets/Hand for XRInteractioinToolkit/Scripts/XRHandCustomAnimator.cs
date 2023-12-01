using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.XR.Hands;

public class Finger
{
    [Flags]
    public enum TYPE
    {
        None = 0,
        Thumb = 1<<0,
        Index = 1<<1,
        Middle = 1<<2,
        Ring = 1<<3,
        Little = 1<<4,
    }
    
    public float CurValue = 0.0f;
    public float TargetValue = 0.0f;
    public readonly int ParameterHash;
    public Finger(TYPE type)
    {
        ParameterHash = Animator.StringToHash(type.ToString());
    }
}

public class XRHandCustomAnimator : MonoBehaviour, ISerializationCallbackReceiver
{
    [SerializeField] private Transform TrackingEventsTransform;
    [SerializeField] private SkinnedMeshRenderer HandMeshRenderer;
    [SerializeField] private Transform RootTransform;
    [SerializeField] private List<JointToTransformReference> JointTransformReferences;
    [Header("Controller Animation Setting")]
    [SerializeField] private Animator HandAnimator;
    [SerializeField] private float AnimationSpeed = 5.0f;
    [SerializeField] private bool UseControllerOffset;
    [SerializeField] private Vector3 PositionOffset;
    [SerializeField] private Vector3 RotationOffset;
    [Header("Animator Input Action")]
    [SerializeField] private InputActionProperty TriggerPressAction;
    [SerializeField] private InputActionProperty GripPressAction;
    [SerializeField] private InputActionProperty PrimaryTouchAction;
    [SerializeField] private InputActionProperty PrimaryClickAction;
    [SerializeField] private InputActionProperty SecondaryTouchAction;
    [SerializeField] private InputActionProperty SecondaryClickAction;
    [SerializeField] private InputActionProperty ThumbStickTouchAction;
    [Header("Fingers")] 
    [SerializeField] private Finger.TYPE GripFingerTypes;
    [SerializeField] private Finger.TYPE TriggerFingerTypes;
    [SerializeField] private Finger.TYPE ThumbFingerTypes;

    private readonly List<Finger> mGripFingers = new();
    private readonly List<Finger> mTriggerFingers = new();
    private readonly List<Finger> mThumbFingers = new();
    private readonly float[] mThumbValues = new float[3];
    private IXRHandEvents mControllerAndHandEvents;
    private Transform[] mJointTransforms;
    private bool[] mHasJointTransformMask;
    private bool mHasRootTransform;
    private NativeArray<Pose> mJointLocalPoses;
    private Quaternion mRotationOffset;
    public List<JointToTransformReference> JointReferences
    {
        get => JointTransformReferences;
        set
        {
            JointTransformReferences = value;
            InitializeFromSerializedReferences();
        }
    }
    public Transform Root
    {
        get => RootTransform;
        set
        {
            RootTransform = value;
            mHasRootTransform = RootTransform != null;
        }
    }

    protected virtual void Awake()
    {
        var fingerTypes = (Finger.TYPE[])Enum.GetValues(typeof(Finger.TYPE));
        foreach (var fingerType in fingerTypes.Where(f=> f !=Finger.TYPE.None))
        {
            SetFinger(GripFingerTypes,fingerType,mGripFingers);
            SetFinger(TriggerFingerTypes,fingerType,mTriggerFingers);
            SetFinger(ThumbFingerTypes,fingerType,mThumbFingers);
        }
        return;

        
        void SetFinger(Finger.TYPE finger, Finger.TYPE compareFinger,ICollection<Finger> fingerCollection)
        {
            if ((finger & compareFinger) == compareFinger)
            {
                fingerCollection.Add(new Finger(compareFinger));
            }
        }
    }

    private void LateUpdate()
    {
        if(HandAnimator.enabled == false) return;
        RootTransform.position = UseControllerOffset ? TrackingEventsTransform.TransformPoint(PositionOffset) : TrackingEventsTransform.position;
        RootTransform.rotation = UseControllerOffset ? TrackingEventsTransform.rotation * Quaternion.Euler(RotationOffset) : TrackingEventsTransform.rotation;
        var isThumbClick = PrimaryClickAction.action.phase == InputActionPhase.Performed || SecondaryClickAction.action.phase == InputActionPhase.Performed;
        mThumbValues[0] = PrimaryTouchAction.action.ReadValue<float>();
        mThumbValues[1] = SecondaryTouchAction.action.ReadValue<float>();
        mThumbValues[2] = ThumbStickTouchAction.action.ReadValue<float>();
        SetFingerTargets(mGripFingers, GripPressAction.action.ReadValue<float>());
        SetFingerTargets(mTriggerFingers, TriggerPressAction.action.ReadValue<float>());
        SetFingerTargets(mThumbFingers,isThumbClick ? 1 : Mathf.Max(mThumbValues) != 0 ? 0.4f :0);
    }
    
    protected virtual void Reset()
    {
        TryGetComponent(out mControllerAndHandEvents);
    }
    protected virtual void OnEnable()
    {
        mJointLocalPoses = new NativeArray<Pose>(XRHandJointID.EndMarker.ToIndex(), Allocator.Persistent);
        mControllerAndHandEvents ??= TrackingEventsTransform.GetComponent<IXRHandEvents>();
        if (mControllerAndHandEvents == null)
        {
            return;
        }
        foreach (var joint in JointTransformReferences)
        {
            var jointIndex = joint.xrHandJointID.ToIndex();
            if (jointIndex < 0 || jointIndex >= mJointTransforms.Length)
            {
                Debug.LogWarning($"{nameof(IXRHandEvents)} has an invalid joint reference set: {joint.xrHandJointID}", this);
            }
        }
        SubscribeToHandTrackingEvents();
    }
    protected virtual void OnDisable()
    {
        if (mJointLocalPoses.IsCreated)
        {
            mJointLocalPoses.Dispose();
        }
        UnsubscribeFromHandTrackingEvents();
    }
    
    private void SetFingerTargets(List<Finger> fingers, float value)
    {
        if(HandAnimator.enabled == false)return;
        var time = AnimationSpeed * Time.unscaledDeltaTime;
        foreach (var finger in fingers)
        {
            finger.TargetValue = value;
            finger.CurValue = Mathf.MoveTowards(finger.CurValue, finger.TargetValue, time);
            HandAnimator.SetFloat(finger.ParameterHash,finger.CurValue);
        }
    }
    private void SubscribeToHandTrackingEvents()
    {
        if (mControllerAndHandEvents == null) return;
        mControllerAndHandEvents.HandTrackingChanged.AddListener(OnHandTrackingChange);
        mControllerAndHandEvents.TrackingChanged.AddListener(OnTrackingChange);
        OnHandTrackingChange(mControllerAndHandEvents.IsHandTracked);
    }
    private void UnsubscribeFromHandTrackingEvents()
    {
        if (mControllerAndHandEvents == null) return;
        mControllerAndHandEvents.HandJointsUpdated.RemoveListener(OnJointsUpdated);
        mControllerAndHandEvents.HandRootPoseUpdated.RemoveListener(OnRootPoseUpdated);
        mControllerAndHandEvents.HandTrackingChanged.RemoveListener(OnHandTrackingChange);
        mControllerAndHandEvents.TrackingChanged.RemoveListener(OnTrackingChange);
    }
    private void OnRootPoseUpdated(Pose pose)
    {
        if (mHasRootTransform == false) return;
        RootTransform.localPosition = pose.position;
        RootTransform.localRotation = pose.rotation;
    }
    private void OnJointsUpdated(XRHandJointsUpdatedEventArgs args)
    {
        if(HandAnimator.enabled) return;
        UpdateJointLocalPoses(args);
        ApplyUpdatedTransformPoses();
    }

    private void OnTrackingChange(bool isTracked)
    {
        HandMeshRenderer.enabled = isTracked;
    }

    private void OnHandTrackingChange(bool isTracked)
    {
        if (isTracked)
        {
            HandAnimator.enabled = false;
            mControllerAndHandEvents.HandJointsUpdated.AddListener(OnJointsUpdated);
            mControllerAndHandEvents.HandRootPoseUpdated.AddListener(OnRootPoseUpdated);
        }
        else
        {
            mControllerAndHandEvents.HandJointsUpdated.RemoveListener(OnJointsUpdated);
            mControllerAndHandEvents.HandRootPoseUpdated.RemoveListener(OnRootPoseUpdated);
            RootTransform.position = UseControllerOffset ? TrackingEventsTransform.TransformPoint(PositionOffset) : TrackingEventsTransform.position;
            RootTransform.rotation = UseControllerOffset ? TrackingEventsTransform.rotation * Quaternion.Euler(RotationOffset) : TrackingEventsTransform.rotation;
            HandAnimator.enabled = true;
        }

    }

    private void UpdateJointLocalPoses(XRHandJointsUpdatedEventArgs args)
    {
        CalculateJointTransformLocalPoses(ref args.hand, ref mJointLocalPoses);
    }
    
    protected virtual void ApplyUpdatedTransformPoses()
    {
        for (var i = 0; i < mJointTransforms.Length; i++)
        {
            if (mHasJointTransformMask[i] == false) continue;
            mJointTransforms[i].SetLocalPose(mJointLocalPoses[i]);
        }
    }
    
    public virtual void FindJointsFromRoot(List<string> missingJointNames)
    {
        FindJointsFromRoot(this, missingJointNames);
    }

    void ISerializationCallbackReceiver.OnBeforeSerialize()
    {
        
    }
    
    void ISerializationCallbackReceiver.OnAfterDeserialize()
    {
        InitializeFromSerializedReferences();
    }

    private void InitializeFromSerializedReferences()
    {
        mHasRootTransform = RootTransform != null;
        if(mHasRootTransform == false) return;
        mHasJointTransformMask = new bool[XRHandJointID.EndMarker.ToIndex()];
        mJointTransforms = new Transform[XRHandJointID.EndMarker.ToIndex()];
        foreach (var joint in JointTransformReferences)
        {
            var jointIndex = joint.xrHandJointID.ToIndex();
            if (jointIndex >= 0 && jointIndex < mJointTransforms.Length)
            {
                mJointTransforms[jointIndex] = joint.jointTransform;
                mHasJointTransformMask[jointIndex] = joint.jointTransform != null;
            }
        }
    }

    #region Static

    private static void FindJointsFromRoot(XRHandCustomAnimator skeletonDriver, List<string> missingJointNames = null)
    {
        void SetupJointTransformReference(XRHandJointID jointId, Transform jointTransform)
        {
            var reference = new JointToTransformReference
            {
                jointTransform = jointTransform,
                xrHandJointID = jointId
            };
            skeletonDriver.JointReferences.Add(reference);
        }

        missingJointNames?.Clear();
        skeletonDriver.JointReferences.Clear();
        Transform wristRootTransform = null;

        // First check if the parent itself is the wrist
        var rootTransformName = skeletonDriver.Root.name;
        if (StartsOrEndsWith(rootTransformName, XRHandJointID.Wrist.ToString()))
            wristRootTransform = skeletonDriver.Root;
        else // Otherwise check under parent for wrist joint as a child
            for (var childIndex = 0; childIndex < skeletonDriver.Root.childCount; ++childIndex)
            {
                var child = skeletonDriver.Root.GetChild(childIndex);
                if (child.gameObject.name.EndsWith(XRHandJointID.Wrist.ToString()))
                    wristRootTransform = child;
            }

        if (wristRootTransform == null)
        {
            missingJointNames?.Add(XRHandJointID.Wrist.ToString());
        }
        else
        {
            SetupJointTransformReference(XRHandJointID.Wrist, wristRootTransform);
            Transform palmTransform = null;
            // Find all the joints under the wrist
            for (var childIndex = 0; childIndex < wristRootTransform.childCount; ++childIndex)
            {
                var child = wristRootTransform.GetChild(childIndex);

                // Find the palm joint
                if (child.name.EndsWith(XRHandJointID.Palm.ToString()))
                {
                    palmTransform = child;
                    continue;
                }

                // Find the finger joints
                for (var fingerIndex = (int)XRHandFingerID.Thumb;
                     fingerIndex <= (int)XRHandFingerID.Little;
                     ++fingerIndex)
                {
                    var fingerId = (XRHandFingerID)fingerIndex;
                    var jointIdFront = fingerId.GetFrontJointID();
                    if (StartsOrEndsWith(child.name, jointIdFront.ToString()) == false) continue;
                    SetupJointTransformReference(jointIdFront, child);
                    var lastChild = child;
                    var jointIndexBack = fingerId.GetBackJointID().ToIndex();

                    // Find the rest of the joints for the finger
                    for (var jointIndex = jointIdFront.ToIndex() + 1; jointIndex <= jointIndexBack; ++jointIndex)
                    {
                        // Find the next child that ends with the joint name
                        var jointName = XRHandJointIDUtility.FromIndex(jointIndex).ToString();
                        for (var nextChildIndex = 0; nextChildIndex < lastChild.childCount; ++nextChildIndex)
                        {
                            var nextChild = lastChild.GetChild(nextChildIndex);
                            if (StartsOrEndsWith(nextChild.name, jointName) == false) continue;
                            lastChild = nextChild;
                            break;
                        }

                        if (StartsOrEndsWith(lastChild.name, jointName))
                        {
                            var jointId = XRHandJointIDUtility.FromIndex(jointIndex);
                            SetupJointTransformReference(jointId, lastChild);
                        }
                        else
                        {
                            missingJointNames?.Add(jointName);
                        }
                    }
                }
            }

            for (var fingerIndex = (int)XRHandFingerID.Thumb; fingerIndex <= (int)XRHandFingerID.Little; ++fingerIndex)
            {
                var fingerId = (XRHandFingerID)fingerIndex;
                var jointIdFront = fingerId.GetFrontJointID();
                // Check if front joint id is present in the list of joint references
                if (skeletonDriver.JointReferences.Any(jointReference =>
                        jointReference.xrHandJointID == jointIdFront)) continue;
                missingJointNames?.Add(jointIdFront.ToString());
            }

            if (palmTransform != null)
                SetupJointTransformReference(XRHandJointID.Palm, palmTransform);
            else
                missingJointNames?.Add(XRHandJointID.Palm.ToString());
        }
    }
    private static bool StartsOrEndsWith(string value, string searchTerm)
    {
        return value.StartsWith(searchTerm, StringComparison.InvariantCultureIgnoreCase) || value.EndsWith(searchTerm, StringComparison.InvariantCultureIgnoreCase);
    }
    
    [BurstCompile]
    private static void CalculateJointTransformLocalPoses(ref XRHand hand, ref NativeArray<Pose> jointLocalPoses)
    {
        var wristIndex = XRHandJointID.Wrist.ToIndex();
        if (hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var wristJointPose))
        {
            jointLocalPoses[wristIndex] = wristJointPose;
            var palmIndex = XRHandJointID.Palm.ToIndex();

            if (hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palmJointPose))
            {
                CalculateLocalTransformPose(wristJointPose, palmJointPose, out var palmPose);
                jointLocalPoses[palmIndex] = palmPose;
            }

            for (var fingerIndex = (int)XRHandFingerID.Thumb;
                 fingerIndex <= (int)XRHandFingerID.Little;
                 ++fingerIndex)
            {
                var parentPose = wristJointPose;
                var fingerId = (XRHandFingerID)fingerIndex;

                var jointIndexBack = fingerId.GetBackJointID().ToIndex();
                var jointIndexFront = fingerId.GetFrontJointID().ToIndex();
                for (var jointIndex = jointIndexFront;
                     jointIndex <= jointIndexBack;
                     ++jointIndex)
                {
                    if (hand.GetJoint((XRHandJointID)(jointIndex+1)).TryGetPose(out var fingerJointPose))
                    {
                        CalculateLocalTransformPose(parentPose, fingerJointPose, out var jointLocalPose);
                        parentPose = fingerJointPose;
                        jointLocalPoses[jointIndex] = jointLocalPose;
                    }
                }
            }
        }
    }
    
    [BurstCompile]
    private static void CalculateLocalTransformPose(in Pose parentPose, in Pose jointPose, out Pose jointLocalPose)
    {
        var inverseParentRotation = Quaternion.Inverse(parentPose.rotation);
        jointLocalPose.position = inverseParentRotation * (jointPose.position - parentPose.position);
        jointLocalPose.rotation = inverseParentRotation * jointPose.rotation;
    }
    
    #endregion

}
