using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.PostProcessing;

public class ReprojectionSettings : MonoBehaviour
{
    
}

[CustomEditor(typeof(ReprojectionSettings))]
public class ReprojectionSettingsInspector : Editor
{
    private SerializedProperty  selectedModeOption;
    private SerializedProperty  simulatedFramerate;
    private SerializedProperty  extrapolatedFramerate;
    private string[] optimizationOptions = { "None", "Motion-to-photon latency reduction", "Frame extrapolation" };
    private string[] reprojectionModes = { "None", "Orientational Timewarp", "Positional Timewarp FE", "Positional Timewarp BE", "Spacewarp" };
    private ReprojectionSettings settings;
    private PostProcessVolume postProcessVolume;

    private void OnEnable()
    {   
        settings = (ReprojectionSettings)target;
        postProcessVolume = settings.GetComponent<PostProcessVolume>();

        // Initialize the SerializedProperty for the selected option
    }

    public override void OnInspectorGUI()
    {
        // Update the serialized object
        serializedObject.Update();

        if(postProcessVolume != null)
        {
            PostProcessProfile profile = postProcessVolume.profile;
            Reprojection reprojection = profile.GetSetting<Reprojection>();

            if(reprojection != null)
            {
                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.LabelField("Optimization option:");

                reprojection.optimizationOption.overrideState = true;
                reprojection.optimizationOption.value = EditorGUILayout.Popup(reprojection.optimizationOption, optimizationOptions);

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space();

                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.LabelField("Reprojection mode:");

                reprojection.reprojectionMode.overrideState = true;
                reprojection.reprojectionMode.value = EditorGUILayout.Popup(reprojection.reprojectionMode, reprojectionModes);

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space();

                EditorGUILayout.LabelField("Framerate Settings:");

                reprojection.fillOutOfScreenOcclusion.overrideState = true;
                reprojection.fillOutOfScreenOcclusion.value = EditorGUILayout.Toggle("Fill Out Of Screen Occlusion", reprojection.fillOutOfScreenOcclusion.value == 1.0f ? true : false) ? 1.0f : 0.0f;

                reprojection.fillDepthOcclusion.overrideState = true;
                reprojection.fillDepthOcclusion.value = EditorGUILayout.Toggle("Fill Depth Occlusion", reprojection.fillDepthOcclusion.value == 1.0f ? true : false) ? 1.0f : 0.0f;

                EditorGUILayout.LabelField("Framerate Settings:");

                EditorGUILayout.BeginHorizontal();
                
                EditorGUILayout.LabelField("Simulated Framerate");
                reprojection.simulatedFramerate.overrideState = true;
                reprojection.simulatedFramerate.value = EditorGUILayout.IntSlider(reprojection.simulatedFramerate, 0, 144);

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                
                EditorGUILayout.LabelField("Extrapolated Framerate");
                reprojection.extrapolatedFramerate.overrideState = true;
                reprojection.extrapolatedFramerate.value = EditorGUILayout.IntSlider(reprojection.extrapolatedFramerate, 0, 144);

                EditorGUILayout.EndHorizontal();

                EditorUtility.SetDirty(profile);
            }
        }

        // Apply changes to the serialized object
        serializedObject.ApplyModifiedProperties();
    }
}
