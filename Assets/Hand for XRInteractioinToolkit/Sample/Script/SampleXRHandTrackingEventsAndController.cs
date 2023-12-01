using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.XR.Interaction.Toolkit;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit;



public class SampleXRHandTrackingEventsAndController : ActionBasedController,IXRHandEvents
{
    [Header("Hand Tracking")]
    [SerializeField] private Handedness Handedness;
    [SerializeField] private XRHandTrackingEvents.UpdateTypes HandUpdateTypes;
    private static readonly List<XRHandSubsystem> sSubsystemList = new List<XRHandSubsystem>();
    private readonly XRHandJointsUpdatedEventArgs mHandJointsUpdatedEventArgs = new XRHandJointsUpdatedEventArgs();
    private XRHandSubsystem mHandSubSystem;

    
    
    public bool IsHandTracked { get; private set; }
    public bool IsTracked { get; private set; }
    public HandUpdatedEvent HandJointsUpdated { get; } = new HandUpdatedEvent();
    public UnityEvent<Pose> HandRootPoseUpdated { get; } = new UnityEvent<Pose>();
    public UnityEvent<bool> HandTrackingChanged { get; } = new UnityEvent<bool>();
    public UnityEvent<bool> TrackingChanged { get; } = new UnityEvent<bool>();
    public UnityEvent HandTrackingAcquired { get; } = new UnityEvent();
    public UnityEvent HandTrackingLost { get; } = new UnityEvent();
    
    
    protected override void OnDisable()
    {
        base.OnDisable();
        UnSubscribeHandSubSystem();
    }
    
    
    protected override void UpdateController()
    {
        UpdateHandSubSystem();
        base.UpdateController();
    }
    
    protected override void UpdateTrackingInput(XRControllerState controllerState)
    {
        if (IsHandTracked == false)
        {
            base.UpdateTrackingInput(controllerState);
        }
        else
        {
            if(controllerState == null) return;
            controllerState.isTracked = true;
            controllerState.inputTrackingState = InputTrackingState.Position | InputTrackingState.Rotation;
            controllerState.position = mHandJointsUpdatedEventArgs.hand.rootPose.position;
            controllerState.rotation = mHandJointsUpdatedEventArgs.hand.rootPose.rotation;
        }
        OnTrackingChange(IsHandTracked, controllerState.isTracked);
    }
    
    private void OnTrackingChange(bool isHandTracking, bool isControllerTracking)
    {
        var oldValue = IsTracked;
        IsTracked = isHandTracking || isControllerTracking;
        if (IsTracked != oldValue)
        {
            TrackingChanged?.Invoke(IsTracked);
        }
    }
    
    private void UpdateHandSubSystem()
    {
        if(mHandSubSystem is { running: true }) return;
        SubsystemManager.GetSubsystems(sSubsystemList);
        foreach (var handSubsystem in sSubsystemList.Where(handSubsystem => handSubsystem.running))
        {
            SetHandSubSystem(handSubsystem);
            break;
        }
    }

    private void SetHandSubSystem(XRHandSubsystem handSubsystem)
    {
        Debug.Log("Set Hand");
        UnSubscribeHandSubSystem();
        mHandJointsUpdatedEventArgs.hand = Handedness switch
        {
            Handedness.Left => handSubsystem.leftHand,
            Handedness.Right => handSubsystem.rightHand,
            Handedness.Invalid => default,
            _ => default
        };
        SubscribeHandSubSystem(handSubsystem);
    }

    private void SubscribeHandSubSystem(XRHandSubsystem handSubsystem)
    {
        mHandSubSystem = handSubsystem;
        mHandSubSystem.trackingAcquired += OnHandTrackingAcquired;
        mHandSubSystem.trackingLost += OnHandTrackingLost;
        mHandSubSystem.updatedHands += OnUpdatedHands;
    }
    private void UnSubscribeHandSubSystem()
    {
        if(mHandSubSystem is null) return;
        mHandSubSystem.trackingAcquired -= OnHandTrackingAcquired;
        mHandSubSystem.trackingLost -= OnHandTrackingLost;
        mHandSubSystem.updatedHands -= OnUpdatedHands;
        mHandSubSystem = null;
    }

    private void OnUpdatedHands(XRHandSubsystem subsystem, XRHandSubsystem.UpdateSuccessFlags updateSuccessFlags, XRHandSubsystem.UpdateType updateEventType)
    {
        if ((updateEventType == XRHandSubsystem.UpdateType.Dynamic && HandUpdateTypes.IsSet(XRHandTrackingEvents.UpdateTypes.Dynamic) == false) 
            || (updateEventType == XRHandSubsystem.UpdateType.BeforeRender && HandUpdateTypes.IsSet(XRHandTrackingEvents.UpdateTypes.BeforeRender) == false))
            return;

        switch (Handedness)
        {
            case Handedness.Left:
            {
                var leftJointsUpdated = (updateSuccessFlags & XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints) != XRHandSubsystem.UpdateSuccessFlags.None;
                var leftRootPoseUpdated = (updateSuccessFlags & XRHandSubsystem.UpdateSuccessFlags.LeftHandRootPose) != XRHandSubsystem.UpdateSuccessFlags.None;

                if (leftJointsUpdated || leftRootPoseUpdated)
                    mHandJointsUpdatedEventArgs.hand = mHandSubSystem.leftHand;
            
                if (leftJointsUpdated)
                    HandJointsUpdated?.Invoke(mHandJointsUpdatedEventArgs);

                if (leftRootPoseUpdated)
                    HandRootPoseUpdated?.Invoke(mHandJointsUpdatedEventArgs.hand.rootPose);
                break;
            }
            case Handedness.Right:
            {
                var rightJointsUpdated = (updateSuccessFlags & XRHandSubsystem.UpdateSuccessFlags.RightHandJoints) != XRHandSubsystem.UpdateSuccessFlags.None;
                var rightRootPoseUpdated = (updateSuccessFlags & XRHandSubsystem.UpdateSuccessFlags.RightHandRootPose) != XRHandSubsystem.UpdateSuccessFlags.None;

                if (rightJointsUpdated || rightRootPoseUpdated)
                    mHandJointsUpdatedEventArgs.hand = mHandSubSystem.rightHand;

                if (rightJointsUpdated)
                    HandJointsUpdated?.Invoke(mHandJointsUpdatedEventArgs);

                if (rightRootPoseUpdated)
                    HandRootPoseUpdated?.Invoke(mHandJointsUpdatedEventArgs.hand.rootPose);
                break;
            }
            case Handedness.Invalid:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void OnHandTrackingAcquired(XRHand hand)
    {
        if(hand.handedness != Handedness) return;
        OnHandTrackingStateChange(true);
    }

    private void OnHandTrackingLost(XRHand hand)
    {
        if(hand.handedness != Handedness) return;
        OnHandTrackingStateChange(false);
    }
    
    private void OnHandTrackingStateChange(bool isTracking)
    {
        var oldValue = IsHandTracked;
        IsHandTracked = isTracking;
        if(oldValue == IsHandTracked) return;
        if (isTracking)
        {
            HandTrackingAcquired?.Invoke();
        }
        else
        {
            HandTrackingLost?.Invoke();
        }
        HandTrackingChanged?.Invoke(isTracking);
    }
    

}
