using UnityEngine;
//using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

#region Effect settings

[System.Serializable]
[PostProcess(typeof(ReprojectionRenderer), PostProcessEvent.BeforeStack, "Reprojection")]
public sealed class Reprojection : PostProcessEffectSettings
{
    //public FloatParameter exampleFloat   = new FloatParameter { value = 1 };
    public IntParameter   optimizationOption = new IntParameter   { value = 0 };
    public IntParameter   reprojectionMode = new IntParameter   { value = 0 };
    public IntParameter   simulatedFramerate = new IntParameter   { value = 30 };
    public IntParameter   extrapolatedFramerate = new IntParameter   { value = 60 };
    public FloatParameter stepSizeFactor   = new FloatParameter { value = 1.0f };
    public FloatParameter maximumStepSize   = new FloatParameter { value = 1.0f };
    public FloatParameter fillOutOfScreenOcclusion   = new FloatParameter { value = 0.0f };
    public FloatParameter fillDepthOcclusion   = new FloatParameter { value = 0.0f };

    // public FloatRangeParameter stepSizeFactor = new FloatRangeParameter(1.0f, 0.0f, 1.0f, false); 
    // public FloatRangeParameter maximumStepSize = new FloatRangeParameter(1.0f, 0.0f, 5.0f, false); 

    //public BoolParameter  useSpacewarp = new BoolParameter { value = false };
}

#endregion

#region Effect renderer

sealed class ReprojectionRenderer : PostProcessEffectRenderer<Reprojection>
{
    static class ShaderIDs
    {
        // Texture buffers
        internal static readonly int PreviousColorTexture               = Shader.PropertyToID("_PreviousColorTexture");
        internal static readonly int PreviousMotionDepthTexture         = Shader.PropertyToID("_PreviousMotionDepthTexture");
        internal static readonly int ReprojectedColorTexture            = Shader.PropertyToID("_ReprojectedColorTexture");
        internal static readonly int ReprojectedMotionDepthTexture      = Shader.PropertyToID("_ReprojectedMotionDepthTexture");
        internal static readonly int MotionVectorHistory                = Shader.PropertyToID("_MotionVectorHistory");

        // Previous camera matrices & vectors
        internal static readonly int PreviousCameraPosition             = Shader.PropertyToID("_PreviousCameraPosition");
        internal static readonly int PreviousInvViewMatrix              = Shader.PropertyToID("_PreviousInvViewMatrix");
        internal static readonly int PreviousInvProjectionMatrix        = Shader.PropertyToID("_PreviousInvProjectionMatrix");
        internal static readonly int PreviousProjectionViewMatrix       = Shader.PropertyToID("_PreviousProjectionViewMatrix");

        // Current camera matrices & vectors
        internal static readonly int CameraPosition                     = Shader.PropertyToID("_CameraPosition");
        internal static readonly int InvViewMatrix                      = Shader.PropertyToID("_InvViewMatrix");
        internal static readonly int InvProjectionMatrix                = Shader.PropertyToID("_InvProjectionMatrix");
        internal static readonly int ProjectionViewMatrix               = Shader.PropertyToID("_ProjectionViewMatrix");

        // Timewarp Settings
        internal static readonly int StepSizeFactor                     = Shader.PropertyToID("_StepSizeFactor");
        internal static readonly int MaximumStepSize                    = Shader.PropertyToID("_MaximumStepSize");

        // Occlusion Settings
        internal static readonly int FillOutOfScreenOcclusion           = Shader.PropertyToID("_FillOutOfScreenOcclusion");
        internal static readonly int FillDepthOcclusion                 = Shader.PropertyToID("_FillDepthOcclusion");
    }

    enum FrameState
    {
        SimulatedFrame,
        ExtrapolatedFrame,
        RegularFrame
    }

    enum OptimizationOption
    {
        None,
        LatencyReduction,
        FrameGeneration
    }

    enum ReprojectionMode
    {
        None = -1,
        OrientationalTimewarp = 0,
        PositionalTimewarpForwardEvaluation = 1,
        PositionalTimewarpBackwardEvaluation = 2,
        AccurateSpacewarp = 3,
    }

    enum ShaderPassId
    {
        OrientationalTimewarp,
        PositionalTimewarpForwardEvaluation,
        PositionalTimewarpBackwardEvaluation,
        AccurateSpacewarp,
        Initialize,
        UpdateMotionVectorHistory,
        ResetMotionVectorHistory,
        Display,
        DisplayPrevious
    }

    ReprojectionMode _currentReprojectionMode;
    OptimizationOption _currentOptimizationOption;

    RenderTexture _previousColorTexture;
    RenderTexture _previousMotionDepthTexture;
    RenderTexture _previousReprojectedTexture;
    RenderTexture _motionVectorHistoryTexture;

    UnityEngine.Rendering.RenderTargetIdentifier[] _mrt = new UnityEngine.Rendering.RenderTargetIdentifier[2];  // Main Render Targets

    // float _prevDeltaTime;

    int _previousSimulatedFramerate;
    int _previousExtrapolatedFramerate;

    float _simulatedFrametime;
    float _extrapolatedFrametime;

    float _accumulatedSimulatedFrameTime;
    float _accumulatedExtrapolatedFrameTime;

    public override void Release()
    {
        if (_previousColorTexture != null)
        {
            RenderTexture.ReleaseTemporary(_previousColorTexture);
            _previousColorTexture = null;
        }

        if (_previousMotionDepthTexture != null)
        {
            RenderTexture.ReleaseTemporary(_previousMotionDepthTexture);
            _previousMotionDepthTexture = null;
        }

        base.Release();
    }

    public override DepthTextureMode GetCameraFlags()
    {
        return DepthTextureMode.MotionVectors | DepthTextureMode.Depth;
    }

    public override void Render(PostProcessRenderContext context)
    {   
        context.command.BeginSample("TemporalReprojection");

        // Set the shader uniforms.
        var sheet = context.propertySheets.Get(Shader.Find("Hidden/Reprojection"));

        // Set spacewarp settings
        sheet.properties.SetFloat(ShaderIDs.StepSizeFactor, settings.stepSizeFactor);
        sheet.properties.SetFloat(ShaderIDs.MaximumStepSize, settings.maximumStepSize);

        // Set occlusion settings
        sheet.properties.SetFloat(ShaderIDs.FillOutOfScreenOcclusion, settings.fillOutOfScreenOcclusion);
        sheet.properties.SetFloat(ShaderIDs.FillDepthOcclusion, settings.fillDepthOcclusion);

        var frameState = EvaluateFrameState();
        _currentReprojectionMode = EvaluateReprojectionMode();
        _currentOptimizationOption = EvaluateOptimizationOption();

        switch (_currentOptimizationOption)
        {
            case OptimizationOption.None:
                {
                    context.command.BlitFullscreenTriangle(context.source, context.destination , sheet, (int) ShaderPassId.Display);
                    break;
                }
            case OptimizationOption.LatencyReduction:
                {
                    if(frameState == FrameState.SimulatedFrame)
                    {
                        ApplyReprojectionLateApplyReprojectionLatency(sheet, context);
                    }

                    UpdateMotionVectorHistory(sheet, context);

                    context.command.BlitFullscreenTriangle(_previousReprojectedTexture, context.destination , sheet, (int) ShaderPassId.Display);
                    break;
                }
            case OptimizationOption.FrameGeneration:
                {
                    if(frameState == FrameState.SimulatedFrame)
                    { 
                        // (Re-)Initializes reference frames and motion vector history
                        InitializeReferenceFrame(sheet, context);
                        // Display current source image
                        context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, (int) ShaderPassId.Display);
                    }

                    if(frameState == FrameState.ExtrapolatedFrame)
                    {
                        // Applies reprojection from the reference image
                        ApplyReprojection(sheet, context);
                    }

                    // Adds current motion vector to motion vector history
                    UpdateMotionVectorHistory(sheet, context);
                    break;
                }
        }

        context.command.EndSample("Reprojection");

        _accumulatedSimulatedFrameTime += Time.deltaTime;
        _accumulatedExtrapolatedFrameTime += Time.deltaTime;
    }

    private void ApplyReprojectionLateApplyReprojectionLatency(PropertySheet sheet, PostProcessRenderContext context)
    {
        // Update the "previous" texture at each frame
        if (_previousColorTexture != null) sheet.properties.SetTexture(ShaderIDs.PreviousColorTexture, _previousColorTexture);
        if (_previousMotionDepthTexture != null) sheet.properties.SetTexture(ShaderIDs.PreviousMotionDepthTexture, _previousMotionDepthTexture);

        // Set camera matrices for the "current" frame
        setCameraMatrices(false, sheet, context);

        // release previous reprojected texture, if it exists
        if (_previousReprojectedTexture != null) RenderTexture.ReleaseTemporary(_previousReprojectedTexture);

        if(_currentReprojectionMode != ReprojectionMode.None) {
            // create new temporary render texture for new reprojected texture
            var newReprojectedTexture = RenderTexture.GetTemporary(context.width, context.height, 0, RenderTextureFormat.ARGBHalf);

            // call render pass with specified reprojection technique
            context.command.BlitFullscreenTriangle(context.source, newReprojectedTexture, newReprojectedTexture.colorBuffer , sheet, (int) _currentReprojectionMode);

            // save reprojection texture for extrapolated frame pass
            _previousReprojectedTexture = newReprojectedTexture;
        } else {
            // create new temporary render texture for previous color
            var newReprojectedTexture = RenderTexture.GetTemporary(context.width, context.height, 0, RenderTextureFormat.ARGBHalf);
            // copy previous color texture to new temporary render texture
            context.command.BlitFullscreenTriangle(_previousColorTexture, newReprojectedTexture, newReprojectedTexture.colorBuffer , sheet, (int) ShaderPassId.Display);
            // set previous color texture to "reprojected texture"
            _previousReprojectedTexture = newReprojectedTexture;
        }

        // display current reprojected frame
        context.command.BlitFullscreenTriangle(_previousReprojectedTexture, context.destination , sheet, (int) ShaderPassId.Display);

        // Discard the previous frame state.
        if (_previousColorTexture != null) RenderTexture.ReleaseTemporary(_previousColorTexture);
        if (_previousMotionDepthTexture != null) RenderTexture.ReleaseTemporary(_previousMotionDepthTexture);
        if (_motionVectorHistoryTexture != null) RenderTexture.ReleaseTemporary(_motionVectorHistoryTexture);

        // Allocate Render Textures for storing the next frame state.
        var newColorTexture = RenderTexture.GetTemporary(context.width, context.height, 0, RenderTextureFormat.ARGBHalf);
        var newMotionDepthTexture = RenderTexture.GetTemporary(context.width, context.height, 0, RenderTextureFormat.ARGBHalf);
        var newMotionVectorHistory = RenderTexture.GetTemporary(context.width, context.height, 0, RenderTextureFormat.ARGBHalf);

        _mrt[0] = newColorTexture.colorBuffer;
        _mrt[1] = newMotionDepthTexture.colorBuffer;

        // Copy the current frame state into the new textures.
        context.command.BlitFullscreenTriangle(context.source, _mrt, newColorTexture.depthBuffer, sheet, (int) ShaderPassId.Initialize);

        // Reset motion vector history
        context.command.BlitFullscreenTriangle(context.source, newMotionVectorHistory, newMotionVectorHistory.colorBuffer, sheet, (int) ShaderPassId.ResetMotionVectorHistory);

        // Update the internal state.
        _previousColorTexture = newColorTexture;
        _previousMotionDepthTexture = newMotionDepthTexture;
        _motionVectorHistoryTexture = newMotionVectorHistory;

        // Set camera matrices for the "previous"
        setCameraMatrices(true, sheet, context);

        sheet.properties.SetTexture(ShaderIDs.MotionVectorHistory, _motionVectorHistoryTexture);
    }

    private void InitializeReferenceFrame(PropertySheet sheet, PostProcessRenderContext context)
    {
        // Discard the previous frame states.
        InitializeReferenceFrameStates(sheet, context);

        // Reset Reference Motion Vector Buffer
        InitializeReferenceMotionVector(sheet, context);

        // Set camera matrices for the "previous"
        setCameraMatrices(true, sheet, context);
    }

    private void InitializeReferenceFrameStates(PropertySheet sheet, PostProcessRenderContext context)
    {
        if (_previousColorTexture != null) RenderTexture.ReleaseTemporary(_previousColorTexture);
        if (_previousMotionDepthTexture != null) RenderTexture.ReleaseTemporary(_previousMotionDepthTexture);

        // Allocate Render Textures for storing the next frame state.
        var newColorTexture = RenderTexture.GetTemporary(context.width, context.height, 0, RenderTextureFormat.ARGBHalf);
        var newMotionDepthTexture = RenderTexture.GetTemporary(context.width, context.height, 0, RenderTextureFormat.ARGBHalf);

        _mrt[0] = newColorTexture.colorBuffer;
        _mrt[1] = newMotionDepthTexture.colorBuffer;

        // Copy the current frame state into the new textures.
        context.command.BlitFullscreenTriangle(context.source, _mrt, newColorTexture.depthBuffer, sheet, (int) ShaderPassId.Initialize);

        // Update the internal state.
        _previousColorTexture = newColorTexture;
        _previousMotionDepthTexture = newMotionDepthTexture;
    }

    private void InitializeReferenceMotionVector(PropertySheet sheet, PostProcessRenderContext context)
    {   
        // release previous render texture
        if (_motionVectorHistoryTexture != null) RenderTexture.ReleaseTemporary(_motionVectorHistoryTexture);
        // create new temporary render texture
        var newMotionVectorHistory = RenderTexture.GetTemporary(context.width, context.height, 0, RenderTextureFormat.ARGBHalf);
        // Set all values to zero
        context.command.BlitFullscreenTriangle(context.source, newMotionVectorHistory, newMotionVectorHistory.colorBuffer, sheet, (int) ShaderPassId.ResetMotionVectorHistory);
        // set new render texture to state
        _motionVectorHistoryTexture = newMotionVectorHistory;
        // set reset texture in shader
        sheet.properties.SetTexture(ShaderIDs.MotionVectorHistory, _motionVectorHistoryTexture);
    }

    private void ApplyReprojection(PropertySheet sheet, PostProcessRenderContext context)
    {
        // Set reference color and motion / depth texture to shader
        if (_previousColorTexture != null) sheet.properties.SetTexture(ShaderIDs.PreviousColorTexture, _previousColorTexture);
        if (_previousMotionDepthTexture != null) sheet.properties.SetTexture(ShaderIDs.PreviousMotionDepthTexture, _previousMotionDepthTexture);

        // Set camera matrices for "current" frame
        setCameraMatrices(false, sheet, context);

        // Apply and display reprojection by currently selected reprojection mode
        if(_currentReprojectionMode != ReprojectionMode.None)
        {
            context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, (int) _currentReprojectionMode);
        } else {
            context.command.BlitFullscreenTriangle(_previousColorTexture, context.destination, sheet, (int) ShaderPassId.Display);
        }
        
    }

    private void UpdateMotionVectorHistory(PropertySheet sheet, PostProcessRenderContext context)
    {
        // Allocate Render Textures for storing the next frame state.
        var newMotionVectorHistory = RenderTexture.GetTemporary(context.width, context.height, 0, RenderTextureFormat.ARGBHalf);

        // Copy the current frame state into the new textures.
        context.command.BlitFullscreenTriangle(context.source, newMotionVectorHistory, sheet, (int) ShaderPassId.UpdateMotionVectorHistory);

        if (_motionVectorHistoryTexture != null) sheet.properties.SetTexture(ShaderIDs.MotionVectorHistory, newMotionVectorHistory);

        // Discard the previous frame state.
        RenderTexture.ReleaseTemporary(_motionVectorHistoryTexture);

        // Update the internal state.
        _motionVectorHistoryTexture = newMotionVectorHistory;
    }

    private void setCameraMatrices(bool previous, PropertySheet sheet, PostProcessRenderContext context)
    {
        if(previous)
        {
            sheet.properties.SetVector(ShaderIDs.PreviousCameraPosition, context.camera.transform.position);
            sheet.properties.SetMatrix(ShaderIDs.PreviousInvViewMatrix, context.camera.worldToCameraMatrix.inverse);
            sheet.properties.SetMatrix(ShaderIDs.PreviousInvProjectionMatrix, context.camera.projectionMatrix.inverse);
            sheet.properties.SetMatrix(ShaderIDs.PreviousProjectionViewMatrix, context.camera.nonJitteredProjectionMatrix * context.camera.worldToCameraMatrix);
        } else {
            sheet.properties.SetVector(ShaderIDs.CameraPosition, context.camera.transform.position);
            sheet.properties.SetMatrix(ShaderIDs.InvViewMatrix, context.camera.worldToCameraMatrix.inverse);
            sheet.properties.SetMatrix(ShaderIDs.InvProjectionMatrix, context.camera.projectionMatrix.inverse);
            sheet.properties.SetMatrix(ShaderIDs.ProjectionViewMatrix, context.camera.nonJitteredProjectionMatrix * context.camera.worldToCameraMatrix);
        }
        
    }

    private ReprojectionMode EvaluateReprojectionMode()
    {
        var val = settings.reprojectionMode.value - 1;
        return (ReprojectionMode)(val);
    }

    private OptimizationOption EvaluateOptimizationOption()
    {
        var val = settings.optimizationOption.value;
        return (OptimizationOption)(val);
    }

    private FrameState EvaluateFrameState() {

        // desides which render pass to select
        // Render Pass 0: depth, motion vectors and color textures are saved at each simulated frame
        // Render Pass 1: in every other subsequent "extrapolated" frame, the previous textures are used to render the scene

        // Example: simulated framerate = 30, extrapolated framerate = 60
        // Renderpass 0: Each 33.3 ms (simulated framerate), the depth, motion vectors and color textures are saved
        // Renderpass 1: Each 16.6 ms (extrapolated framerate), the previous textures are used to render the scene by applying timewarp and spacewarp
        // In short, every second frame is rendered by extrapolating / warping the previous frame

        // calculate the frametime for simulated and extrapolated frames
        _simulatedFrametime = 1f / (int) settings.simulatedFramerate;
        _extrapolatedFrametime = 1f / (int) settings.extrapolatedFramerate;

        // check if the accumulated frametimes have pass the frametime threshold
        var passedSimulatedFrameTime = _accumulatedSimulatedFrameTime >= _simulatedFrametime;
        var passedExtrapolatedFrameTime = _accumulatedExtrapolatedFrameTime >= _extrapolatedFrametime;

        // checks if post process settings for framerates have changed
        if(settings.simulatedFramerate != _previousSimulatedFramerate || settings.extrapolatedFramerate != _previousExtrapolatedFramerate)
        {  
            // set new framerates
            _previousSimulatedFramerate = settings.simulatedFramerate;
            _previousExtrapolatedFramerate = settings.extrapolatedFramerate;
            
            // reset accumulated frametimes
            // set accumulated simulated frametime to be equal the simulated frametime, so that new frames always start with first render pass
            _accumulatedSimulatedFrameTime = _simulatedFrametime; 
            _accumulatedExtrapolatedFrameTime = 0;

            // set passed frametime booleans
            passedSimulatedFrameTime = true;
            passedExtrapolatedFrameTime = false;
        }

        // if simulated or extrapolated frametime has passed
        if(passedSimulatedFrameTime || passedExtrapolatedFrameTime){
            var setupPass = FrameState.SimulatedFrame;

            if( passedExtrapolatedFrameTime ) {
                // reduce the accumulated frametime by the extrapolated frametime
                _accumulatedExtrapolatedFrameTime -= _extrapolatedFrametime;
                setupPass = FrameState.ExtrapolatedFrame;
            }

            // if both frametimes have passed, the simulated frametime will have the higher priority
            if( passedSimulatedFrameTime ) { 
                // reduce the accumulated frametime by the simulated frametime
                _accumulatedSimulatedFrameTime -= _simulatedFrametime;
                setupPass = FrameState.SimulatedFrame;
            }

            return setupPass;
        }

        // the frametime threshold has not been passed / the frame rendering was too fast
        return FrameState.RegularFrame;
    }
}

#endregion