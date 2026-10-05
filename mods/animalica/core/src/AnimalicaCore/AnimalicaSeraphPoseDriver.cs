using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace AnimalicaCore;

internal sealed class AnimalicaSeraphPoseDriver
{
    private const float CoreCameraBaseOffsetY = -0.10f;
    private const float CoreCameraBaseOffsetZ = -1.00f;

    private static readonly AssetLocation SeraphShapeLocation = new(
        "game",
        "shapes/entity/humanoid/seraph.json"
    );

    private readonly ICoreClientAPI api;
    private readonly float[] mainHandParentTransform = Mat4f.Create();
    private readonly float[] offHandParentTransform = Mat4f.Create();
    private readonly Dictionary<string, AnimationMetaData> mirroredAnimations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly float[] relativePoseBuffer = Mat4f.Create();
    private readonly float[] inverseModelBuffer = Mat4f.Create();
    private readonly float[] inverseViewBuffer = Mat4f.Create();
    private readonly float[] cameraPoseBuffer = Mat4f.Create();
    private readonly float[] worldPoseBuffer = Mat4f.Create();
    private readonly float[] rightHandAnimationMatrix = Mat4f.Create();
    private readonly float[] leftHandAnimationMatrix = Mat4f.Create();

    private EntityPlayer? boundPlayer;
    private ClientAnimator? animator;
    private float[]? defaultRightHandInverse;
    private float[]? defaultLeftHandInverse;
    private long nextInitializationAttemptMs;
    private bool failureLogged;

    public AnimalicaSeraphPoseDriver(ICoreClientAPI api, AnimalicaFirstPersonConfig config)
    {
        this.api = api;
        UpdateTransforms(config);
    }

    public void UpdateTransforms(AnimalicaFirstPersonConfig config)
    {
        BuildParentTransform(
            mainHandParentTransform,
            config.SeraphYawDegrees,
            config.MainHandOffsetX,
            config.MainHandOffsetY,
            config.MainHandOffsetZ
        );
        BuildParentTransform(
            offHandParentTransform,
            config.SeraphYawDegrees,
            config.OffHandOffsetX,
            config.OffHandOffsetY,
            config.OffHandOffsetZ
        );
    }

    public void Update(Entity entity, float dt)
    {
        if (entity is not EntityPlayer player)
        {
            return;
        }

        if (!ReferenceEquals(boundPlayer, player))
        {
            Reset(player);
        }

        if (animator == null && !TryInitialize(player))
        {
            return;
        }

        try
        {
            mirroredAnimations.Clear();
            foreach ((string animationCode, AnimationMetaData liveMetadata) in player.AnimManager.ActiveAnimationsByAnimCode)
            {
                if (liveMetadata == null)
                {
                    continue;
                }

                string proxyAnimationCode = string.IsNullOrWhiteSpace(liveMetadata.Animation)
                    ? animationCode
                    : liveMetadata.Animation;
                if (animator!.GetAnimationState(proxyAnimationCode) == null)
                {
                    continue;
                }

                AnimationMetaData proxyMetadata = liveMetadata.Clone();
                proxyMetadata.StartFrameOnce = 0f;
                proxyMetadata.AnimationSounds = null;
                mirroredAnimations[proxyAnimationCode] = proxyMetadata;
            }

            animator!.OnFrame(mirroredAnimations, GameMath.Clamp(dt, 0f, 0.1f));
        }
        catch (Exception exception)
        {
            LogFailure("updating the Seraph pose proxy", exception);
            Reset(player);
            nextInitializationAttemptMs = api.ElapsedMilliseconds + 5000;
        }
    }

    public bool TryGetHandPose(
        bool leftHand,
        float[] currentModelMatrix,
        out float[] animationMatrix
    )
    {
        animationMatrix = Array.Empty<float>();
        float[]? defaultInverse = leftHand ? defaultLeftHandInverse : defaultRightHandInverse;
        float[]? viewMatrix = api.Render.CameraMatrixOriginf;
        if (animator == null
            || defaultInverse == null
            || currentModelMatrix == null
            || currentModelMatrix.Length < 16
            || viewMatrix == null
            || viewMatrix.Length < 16)
        {
            return false;
        }

        AttachmentPointAndPose? pose = animator.GetAttachmentPointPose(
            leftHand ? "LeftHand" : "RightHand"
        );
        if (pose?.AnimModelMatrix == null || pose.AnimModelMatrix.Length < 16)
        {
            return false;
        }

        float[] relativePose = relativePoseBuffer;
        Mat4f.Multiply(relativePose, defaultInverse, pose.AnimModelMatrix);

        float[] inverseModel = inverseModelBuffer;
        float[] inverseView = inverseViewBuffer;
        if (Mat4f.Invert(inverseModel, currentModelMatrix) == null
            || Mat4f.Invert(inverseView, viewMatrix) == null)
        {
            return false;
        }

        // Preserve the camera-space target accepted in the standalone test,
        // including Core's neutral held-item base. Converting it back through
        // the active model matrix keeps it independent of model origin, eye
        // height, shape, and body scale.
        float[] cameraPose = cameraPoseBuffer;
        Mat4f.Identity(cameraPose);
        Mat4f.Translate(
            cameraPose,
            cameraPose,
            0f,
            CoreCameraBaseOffsetY,
            CoreCameraBaseOffsetZ
        );
        Mat4f.Multiply(
            cameraPose,
            cameraPose,
            leftHand ? offHandParentTransform : mainHandParentTransform
        );
        Mat4f.Multiply(cameraPose, cameraPose, relativePose);

        float[] worldPose = worldPoseBuffer;
        Mat4f.Multiply(worldPose, inverseView, cameraPose);

        animationMatrix = leftHand ? leftHandAnimationMatrix : rightHandAnimationMatrix;
        Mat4f.Multiply(animationMatrix, inverseModel, worldPose);
        return true;
    }

    private static void BuildParentTransform(
        float[] destination,
        float yawDegrees,
        float offsetX,
        float offsetY,
        float offsetZ
    )
    {
        Mat4f.Identity(destination);
        Mat4f.Translate(destination, destination, offsetX, offsetY, offsetZ);
        Mat4f.RotateY(destination, destination, yawDegrees * GameMath.DEG2RAD);
    }

    private bool TryInitialize(EntityPlayer player)
    {
        if (api.ElapsedMilliseconds < nextInitializationAttemptMs)
        {
            return false;
        }

        try
        {
            Shape? shape = Shape.TryGet(api, SeraphShapeLocation);
            if (shape?.Animations == null || shape.Elements == null)
            {
                throw new InvalidOperationException($"Could not load {SeraphShapeLocation} with animations and elements.");
            }

            RetainHandAttachmentPoints(shape.Elements);
            shape.InitForAnimations(api.Logger, SeraphShapeLocation.ToString());
            ClientAnimator proxy = new(
                () => player.Controls.MovespeedMultiplier * player.GetWalkSpeedMultiplier(0.3),
                shape.Animations,
                shape.Elements,
                shape.JointsById
            );

            mirroredAnimations.Clear();
            proxy.OnFrame(mirroredAnimations, 0f);

            animator = proxy;
            defaultRightHandInverse = InvertRestPose(proxy, "RightHand");
            defaultLeftHandInverse = InvertRestPose(proxy, "LeftHand");
            failureLogged = false;
            api.Logger.Notification(
                "[AnimalicaCore] Private Seraph first-person pose proxy initialized with {0} animations.",
                shape.Animations.Length
            );
            return true;
        }
        catch (Exception exception)
        {
            LogFailure("initializing the Seraph pose proxy", exception);
            nextInitializationAttemptMs = api.ElapsedMilliseconds + 5000;
            return false;
        }
    }

    private static void RetainHandAttachmentPoints(ShapeElement[] elements)
    {
        foreach (ShapeElement element in elements)
        {
            if (element.AttachmentPoints != null)
            {
                element.AttachmentPoints = Array.FindAll(
                    element.AttachmentPoints,
                    point => string.Equals(point.Code, "RightHand", StringComparison.Ordinal)
                        || string.Equals(point.Code, "LeftHand", StringComparison.Ordinal)
                );
            }

            if (element.Children != null)
            {
                RetainHandAttachmentPoints(element.Children);
            }
        }
    }

    private static float[] InvertRestPose(ClientAnimator proxy, string attachmentCode)
    {
        AttachmentPointAndPose? pose = proxy.GetAttachmentPointPose(attachmentCode);
        if (pose?.AnimModelMatrix == null || pose.AnimModelMatrix.Length < 16)
        {
            throw new InvalidOperationException($"The vanilla Seraph shape has no usable {attachmentCode} attachment pose.");
        }

        float[] inverse = Mat4f.Create();
        if (Mat4f.Invert(inverse, pose.AnimModelMatrix) == null)
        {
            throw new InvalidOperationException($"The vanilla Seraph {attachmentCode} rest-pose matrix is not invertible.");
        }

        return inverse;
    }

    private void Reset(EntityPlayer player)
    {
        boundPlayer = player;
        animator = null;
        defaultRightHandInverse = null;
        defaultLeftHandInverse = null;
        mirroredAnimations.Clear();
    }

    private void LogFailure(string operation, Exception exception)
    {
        if (failureLogged)
        {
            return;
        }

        failureLogged = true;
        api.Logger.Warning(
            "[AnimalicaCore] Failed while {0}; Core's neutral held-item pose remains active. {1}",
            operation,
            exception.Message
        );
    }
}
