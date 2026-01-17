using System;
using TMPro;
using UnityEngine;

public class UIExamplePoints : MonoBehaviour
{
    [SerializeField] public UIManager uiManager;

    [Serializable]
    public struct ExampleFile
    {
        public string name;
        public TextAsset json;
    }

    [Header("UI")]
    [SerializeField] private TMP_Dropdown dropdown;

    [Header("Example JSON files")]
    [SerializeField] private ExampleFile[] exampleFiles;

    private void Awake()
    {
        if (uiManager == null)
        uiManager = GetComponent<UIManager>();

        dropdown.ClearOptions();
        var options = new System.Collections.Generic.List<string>(exampleFiles.Length);
        foreach (var f in exampleFiles)
        {
            options.Add(string.IsNullOrWhiteSpace(f.name) ? f.json.name : f.name);
        }
        dropdown.AddOptions(options);

        dropdown.onValueChanged.AddListener(OnChanged);

        OnChanged(dropdown.value);
    }

    private void OnDestroy()
    {
        dropdown.onValueChanged.RemoveListener(OnChanged);
    }

    private void OnChanged(int index)
    {
        if (index < 1 || index >= exampleFiles.Length || exampleFiles[index].json == null)
            return;

        var selectedName = string.IsNullOrWhiteSpace(exampleFiles[index].name) ? exampleFiles[index].json.name : exampleFiles[index].name;
        var json = exampleFiles[index].json.text;

        Debug.Log($"Selected example file: {selectedName} ({json.Length} chars)");

        uiManager.ApplyJSON(json, uiManager.GetUseStoredColorsFlag());

    }
}
