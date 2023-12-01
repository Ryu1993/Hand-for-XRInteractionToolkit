using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit;

public interface IXRHandEvents
{
    public bool IsHandTracked { get;}
    public HandUpdatedEvent HandJointsUpdated { get; }
    public UnityEvent<Pose> HandRootPoseUpdated { get; }
    public UnityEvent<bool> HandTrackingChanged { get; }
    public UnityEvent<bool> TrackingChanged { get; }
   
}
