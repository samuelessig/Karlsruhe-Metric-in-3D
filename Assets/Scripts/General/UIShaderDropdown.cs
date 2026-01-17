using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class UIShaderDropdown : MonoBehaviour
{
    [Header("UI Dropdown")]
    [SerializeField]
    private TMP_Dropdown shaderDropdown;

    [Header("Target")]
    [SerializeField]
    private SphereGenerator sphereGenerator;

    [Header("Available Shaders")]
    [Tooltip("Drag and drop the shaders here that should be selectable in the dropdown")]
    [SerializeField]
    private Shader[] availableShaders;

    private void Awake()
    {
        if (!shaderDropdown)
        {
            Debug.LogError("[UIShaderDropdown] TMP_Dropdown is not wired");
        }

        if (!sphereGenerator)
        {
            Debug.LogError("[UIShaderDropdown] SphereGenerator is not wired");
        }
    }

    private void Start()
    {
        if (!shaderDropdown || !sphereGenerator)
        {
            return;
        }

        PopulateDropdown();
        SelectInitialShader();
        shaderDropdown.onValueChanged.AddListener(OnShaderSelected);
    }

    private void PopulateDropdown()
    {
        shaderDropdown.ClearOptions();

        var options = new List<string>();
        if (availableShaders != null)
        {
            foreach (var shader in availableShaders)
            {
                if (shader == null)
                {
                    continue;
                }

                options.Add(shader.name.Replace("Custom/", ""));
            }
        }

        shaderDropdown.AddOptions(options);
    }

    private void SelectInitialShader()
    {
        if (availableShaders == null || availableShaders.Length == 0)
            return;

        // Try to find the current SphereGenerator's voronoiShader
        int index = 0;
        if (sphereGenerator.voronoiShader != null)
        {
            for (int i = 0; i < availableShaders.Length; i++)
            {
                if (availableShaders[i] == sphereGenerator.voronoiShader)
                {
                    index = i;
                    break;
                }
            }
        }

        shaderDropdown.value = index;
        shaderDropdown.RefreshShownValue();

        // Apply once at startup so material and shader are in sync
        ApplyShaderByIndex(index);
    }

    private void OnShaderSelected(int index)
    {
        ApplyShaderByIndex(index);
    }

    private void ApplyShaderByIndex(int index)
    {
        if (availableShaders == null ||
            index < 0 || index >= availableShaders.Length)
        {
            Debug.LogWarning("[UIShaderDropdown] Invalid shader index: " + index);
            return;
        }

        Debug.Log("[UIShaderDropdown] Applying shader: " + availableShaders[index].name);
        var shader = availableShaders[index];
        sphereGenerator.ApplyShader(shader);
    }
}