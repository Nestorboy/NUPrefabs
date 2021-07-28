﻿
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using System;
using TMPro;

namespace UdonSharp.Nessie.Debugger
{
    [AddComponentMenu("Udon Sharp/Nessie/Debugger/NUDebugger")]
    public class NUDebugger : UdonSharpBehaviour
    {
        #region DebugFields

        [HideInInspector] public Component[] ArrUdons = new Component[0];
        [HideInInspector] public string[] ArrType = new string[] { "Array", "Variable", "Event" };
        [HideInInspector] public string[][] ArrNames = new string[0][];
        [HideInInspector] public string[][] VarNames = new string[0][];
        [HideInInspector] public string[][] EntNames = new string[0][];

        #endregion DebugFields

        #region SerializedFields

        [Header("Debug Button")]

        [Tooltip("Transform used as parent for all DebugButton objects.")]
        [SerializeField] private Transform _buttonContainer = null;

        [Tooltip("Prefab used to instantiate new DebugButton objects.")]
        [SerializeField] private GameObject _buttonPrefab = null;



        [Header("Debug Text")]

        [Tooltip("Transform used as a parent for all the DebugText objects.")]
        [SerializeField] private Transform _textContainer = null;

        [Tooltip("Transform used as pivot for _poolDebugText prefab placement.")]
        [SerializeField] private Transform _textTarget = null;

        [Tooltip("Prefab used to instantiate new DebugText objects.")]
        [SerializeField] private GameObject _textPrefab = null;



        [Header("Menu Buttons")]

        [Tooltip("Animator used to animate button transitions.")]
        [SerializeField] private Animator _targetAnimator = null;

        [Tooltip("Text field used for displaying selected udon.")]
        [SerializeField] private TextMeshProUGUI _udonField = null;

        [Tooltip("Text field used for displaying selected variable.")]
        [SerializeField] private TextMeshProUGUI _typeField = null;

        [Tooltip("Text field used for displaying variable name.")]
        [SerializeField] private UnityEngine.UI.InputField _textField = null;



        [Header("Extra Buttons")]

        [Tooltip("Pointer used to get values for the UpdateRate setting.")]
        [SerializeField] private UnityEngine.UI.Slider _settingUpdateRate = null;

        [Tooltip("Text field used for displaying UpdateRate status.")]
        [SerializeField] private TextMeshProUGUI _updateRateField = null;

        [Tooltip("Pointer used to get values for the Networked setting.")]
        [SerializeField] private UnityEngine.UI.Toggle _settingNetworked = null;

        [Tooltip("Text field used for displaying Networking status.")]
        [SerializeField] private TextMeshProUGUI _networkedField = null;

        #endregion SerializedFields

        #region PublicFields

        [HideInInspector] public bool CustomUdon = false;
        [HideInInspector] public int UdonID = -1;
        [HideInInspector] public int TypeID = -1;
        [HideInInspector] public int MenuID = 0;
        [HideInInspector] public int ButtonID = 0;

        // Settings. (Yes, I was too lazy to make the settings modular.)

        [HideInInspector] public Color _mainColor = new Color(0f, 1f, 0.6235294f, 1f);
        [HideInInspector] public Color _crashColor = new Color(1f, 0.2705882f, 0.5294118f, 1f);
        [HideInInspector] public float _updateRate = 0.2f;
        [HideInInspector] public bool _networked = false;

        #endregion PublicFields

        #region PrivateFields

        private UdonBehaviour _currentUdon;

        private object[] _currentArray;

        private NUDebuggerText[] _poolDebugText;
        private NUDebuggerButton[] _poolDebugButton;

        private bool _crashed = false;

        private GameObject[] _poolSettingButton;

        #endregion PrivateFields

        private void Start()
        {
            _poolDebugText = new NUDebuggerText[0];
            _poolDebugButton = new NUDebuggerButton[0];

            _poolSettingButton = new GameObject[_buttonContainer.childCount];
            for (int i = 0; i < _poolSettingButton.Length; i++)
                _poolSettingButton[i] = _buttonContainer.GetChild(i).gameObject;

            _SetColor(gameObject);
            _SetColor(_buttonPrefab);
            _SetColor(_textPrefab);

            _UpdateButtons();

            _UpdateLoop();
        }

        public void _UpdateLoop()
        {
            // Change color if specified UdonBehaviour ran into an error.
            if (UdonID >= 0)
            {
                if (Utilities.IsValid(_currentUdon))
                {
                    if (UdonID >= 0 && !_crashed)
                    {
                        if (_crashed = !_currentUdon.enabled)
                        {
                            _udonField.color = _crashColor;
                        }
                    }
                }
                else // UdonBehaviour removed.
                {
                    if (CustomUdon)
                    {
                        UdonID = -1;
                        MenuID = 0;

                        _udonField.text = "[Removed]";
                        _udonField.color = _crashColor;

                        _UpdateButtons();
                    }
                    else
                    {
                        _RemoveUdon(UdonID);
                    }
                }
            }

            // Update debug windows.
            for (int i = 0; i < _poolDebugText.Length;)
            {
                if (!Utilities.IsValid(_poolDebugText[i]))
                {
                    Debug.Log("[<color=#00FF9F>NUDebugger</color>] Removing missing pool object from pool array.");

                    object[] bufferArray = ListRemove(_poolDebugText, i);
                    _poolDebugText = new NUDebuggerText[bufferArray.Length];
                    bufferArray.CopyTo(_poolDebugText, 0);

                    continue;
                }

                if (_poolDebugText[i].gameObject.activeSelf)
                {
                    _poolDebugText[i]._UpdateText();
                }

                i++;
            }

            SendCustomEventDelayedSeconds(nameof(_UpdateLoop), _updateRate);
        }



        // Menu interaction functions.
        private void _UpdateButtons()
        {
            // Avoid turning on buttons for custom Udon target, or settings menu.
            if (MenuID > 2 && CustomUdon || MenuID == 2)
            {
                for (int i = 0; i < _poolDebugButton.Length; i++)
                {
                    _poolDebugButton[i].gameObject.SetActive(false);
                }

                if (MenuID == 2)
                {
                    for (int i = 0; i < _poolSettingButton.Length; i++)
                    {
                        _poolSettingButton[i].SetActive(true);
                    }

                    _updateRateField.text = $"Update Rate: {_updateRate} s";
                    _networkedField.text = $"Networked: {_networked}";
                }
                else if (_targetAnimator.GetInteger("MenuID") == 2)
                {
                    for (int i = 0; i < _poolSettingButton.Length; i++)
                    {
                        _poolSettingButton[i].SetActive(false);
                    }
                }

                _targetAnimator.SetInteger("MenuID", MenuID);

                return;
            }

            // Turn off settings buttons if user exits settings.
            if (_targetAnimator.GetInteger("MenuID") == 2)
            {
                for (int i = 0; i < _poolSettingButton.Length; i++)
                {
                    _poolSettingButton[i].SetActive(false);
                }
            }

            switch (MenuID)
            {
                case 0: // Udon selection.

                    for (int i = 0; i < ArrUdons.Length; i++)
                        if (!Utilities.IsValid(ArrUdons[i]))
                            _RemoveUdon(i);

                    _currentArray = ArrUdons;

                    break;

                case 1: // Type selection.

                    _currentArray = ArrType;

                    break;

                case 3: // Array selection.

                    _currentArray = ArrNames[UdonID];

                    break;

                case 4: // Variable selection

                    _currentArray = VarNames[UdonID];

                    break;

                case 5: // Event selection

                    _currentArray = EntNames[UdonID];

                    break;

                default:

                    Debug.LogWarning($"[<color=#00FF9F>NUDebugger</color>] No debug type specified.");

                    _currentArray = new object[0];

                    break;
            }

            int buttonCount = 0;

            if (_currentArray != null)
                buttonCount = _currentArray.Length;

            // Prepare buttons.
            if (_poolDebugButton.Length < buttonCount)
            {
                NUDebuggerButton[] newPool = new NUDebuggerButton[buttonCount];
                _poolDebugButton.CopyTo(newPool, 0);

                for (int i = _poolDebugButton.Length; i < buttonCount; i++)
                {
                    GameObject newObject = VRCInstantiate(_buttonPrefab);

                    newObject.transform.SetParent(_buttonContainer, false);

                    newPool[i] = newObject.GetComponent<NUDebuggerButton>();
                    newPool[i].TargetUdon = this;
                    newPool[i].ButtonID = i;
                }

                _poolDebugButton = newPool;
            }

            for (int i = 0; i < _poolDebugButton.Length; i++)
            {
                _poolDebugButton[i].gameObject.SetActive(i < buttonCount);

                if (i < buttonCount)
                {
                    // Change color if specified UdonBehaviour ran into an error.
                    Color textColor;
                    string textName;

                    if (MenuID == 0)
                    {
                        if (((UdonBehaviour)_currentArray[i]).enabled) // Check if UdonBehaviour hasn't '_crashed'.
                            textColor = _mainColor;
                        else
                            textColor = _crashColor;

                        textName = ((UdonBehaviour)_currentArray[i]).name;
                        string typeName = ((UdonSharpBehaviour)_currentArray[i]).GetUdonTypeName();
                        if (typeName != "UnknownType")
                            textName += $" ({typeName})";
                    }
                    else
                    {
                        textColor = _mainColor;

                        textName = _currentArray[i].ToString();

                        if (MenuID == 3 || MenuID == 4)
                        {
                            object value = ((UdonBehaviour)ArrUdons[UdonID]).GetProgramVariable((string)_currentArray[i]);
                            string icon;

                            if (Utilities.IsValid(value))
                                icon = _CheckType(value.GetType());
                            else
                                icon = _CheckType(((UdonBehaviour)ArrUdons[UdonID]).GetProgramVariableType((string)_currentArray[i]));
                            textName = $"{icon} {textName}";
                        }
                    }

                    _poolDebugButton[i].TargetText.color = textColor;
                    _poolDebugButton[i].TargetText.text = textName;
                }
            }

            _targetAnimator.SetInteger("MenuID", MenuID);
        }

        private string _CheckType(Type type)
        {
            // Debug.Log($"[<color=#00FF9F>NUDebugger</color>] Type: {type}\nName: {type.Name}\nFullName: {type.FullName}\nNamespace: {type.Namespace}\nAssembly: {type.AssemblyQualifiedName}\nGUID: {type.GUID}\nHash: {type.GetHashCode()}");

            string name = type.Name;
            string space = type.Namespace;
            string spriteName = "Object";
            
            if (space == "System")
            {
                if (name.Contains("Boolean")) spriteName = "Bool";
                else if (name.Contains("Int")) spriteName = "Int";
                else if (name.Contains("Single") || name.Contains("Double")) spriteName = "Float";
                else if (name.Contains("String")) spriteName = "String";
            }
            else
            { 
                if (name.Contains("Transform")) spriteName = "Transform";
                else if (name.Contains("Texture")) spriteName = "Texture";
                else if (name.Contains("Material")) spriteName = "Material";
                else if (name.Contains("Light")) spriteName = "Light";
                else if (name.Contains("Audio")) spriteName = "Audio";
                else if (name.Contains("Animat")) spriteName = "Animation";
                else if (name.Contains("Camera")) spriteName = "Camera";
                else if (name.Contains("Particle")) spriteName = "Particle";
                else if (name.Contains("Mesh") && space != "TextMeshPro") spriteName = "Mesh";
                else if (name.Contains("Udon")) spriteName = "Udon";
                else if (name.Contains("PlayerApi")) spriteName = "Player";
            }

            return $"<sprite name={spriteName}\" tint>";
        }

        private void _CheckSelected()
        {
            if (UdonID < 0)
                MenuID = 0;
            else if (TypeID < 0)
                MenuID = 1;
            else
                MenuID = TypeID + 3;
        }

        public void _SelectID()
        {
            switch (MenuID)
            {
                case 0: // Udon selection.

                    UdonBehaviour newUdon = (UdonBehaviour)ArrUdons[ButtonID];

                    if (!Utilities.IsValid(newUdon))
                    {
                        _RemoveUdon(ButtonID);

                        return;
                    }

                    CustomUdon = false;
                    _currentUdon = newUdon;

                    // Change color if specified UdonBehaviour ran into an error.
                    Color textColor;
                    if (_crashed = !_currentUdon.enabled)
                        textColor = _crashColor;
                    else
                        textColor = _mainColor;

                    _udonField.color = textColor;

                    // Update the UdonTarget label.
                    string textName = _currentUdon.name;
                    string typeName = ((UdonSharpBehaviour)(Component)_currentUdon).GetUdonTypeName();

                    if (typeName != "UnknownType")
                        textName += $" ({typeName})";

                    _udonField.text = textName;

                    UdonID = ButtonID;

                    _CheckSelected();
                    _UpdateButtons();

                    break;

                case 1: // Type selection.

                    _typeField.text = ArrType[ButtonID];

                    TypeID = ButtonID;

                    _CheckSelected();
                    _UpdateButtons();

                    break;

                case 2: // Setting selection.

                    if (ButtonID == 0)
                    {
                        _updateRate = _settingUpdateRate.value / 20;

                        _updateRateField.text = $"Update Rate: {_updateRate} s";
                    }
                    else if (ButtonID == 1)
                    {
                        _networked = _settingNetworked.isOn;

                        _networkedField.text = $"Networked: {_networked}";
                    }

                    break;

                case 3: // Array selection.

                    _textField.text = ArrNames[UdonID][ButtonID];

                    _targetAnimator.SetTrigger("Button/Name");

                    _SelectName();

                    break;

                case 4: // Variable selection

                    _textField.text = VarNames[UdonID][ButtonID];

                    _targetAnimator.SetTrigger("Button/Name");

                    _SelectName();

                    break;

                case 5: // Event selection

                    _textField.text = EntNames[UdonID][ButtonID];

                    _targetAnimator.SetTrigger("Button/Name");

                    _SelectName();

                    break;

                default:

                    Debug.LogWarning($"[<color=#00FF9F>NUDebugger</color>] No debug type specified.");

                    _currentArray = new object[0];

                    break;
            }
        }

        public void _SelectUdon()
        {
            if (MenuID == 0)
                _CheckSelected();
            else
                MenuID = 0;

            _UpdateButtons();
        }

        public void _SelectType()
        {
            if (MenuID == 1)
                _CheckSelected();
            else
                MenuID = 1;

            _UpdateButtons();
        }

        public void _SelectSettings()
        {
            if (MenuID == 2)
                _CheckSelected();
            else
            { 
                MenuID = 2;

                _settingUpdateRate.value = _updateRate * 20;
                _settingNetworked.isOn = _networked;
            }

            _UpdateButtons();
        }

        public void _SelectName()
        {
            if (MenuID == 0)
            {
                GameObject newGameObject = GameObject.Find(_textField.text);

                if (Utilities.IsValid(newGameObject))
                {
                    UdonBehaviour newUdon = (UdonBehaviour)newGameObject.GetComponent(typeof(UdonBehaviour));

                    if (Utilities.IsValid(newUdon))
                        _SelectCustomUdon(newUdon);
                    else
                        Debug.LogWarning($"[<color=#00FF9F>NUDebugger</color>] No UdonBehaviour found on {newGameObject}");
                }
                else
                    Debug.LogWarning($"[<color=#00FF9F>NUDebugger</color>] No active GameObject found by the name of {_textField.text}");

                return;
            }
            else if (UdonID < 0)
            {
                MenuID = 0;

                _UpdateButtons();

                return;
            }
            else if (TypeID < 0)
            {
                MenuID = 1;

                _UpdateButtons();

                return;
            }
            
            switch (TypeID)
            {
                case 0:

                    _DebugArray(_currentUdon, _textField.text);

                    break;

                case 1:

                    _DebugVariable(_currentUdon, _textField.text);

                    break;

                case 2:

                    _DebugEvent(_currentUdon, _textField.text);

                    break;
            }

            _targetAnimator.SetTrigger("Button/Enter");
        }

        public void _SelectCustomUdon(UdonBehaviour udon)
        {
            CustomUdon = true;
            _currentUdon = udon;

            // Change color if specified UdonBehaviour ran into an error.
            Color textColor;
            if (_crashed = !_currentUdon.enabled)
                textColor = _crashColor;
            else
                textColor = _mainColor;

            _udonField.color = textColor;

            // Update the UdonTarget label.
            string textName = _currentUdon.name;
            string typeName = ((UdonSharpBehaviour)(Component)_currentUdon).GetUdonTypeName();

            if (typeName != "UnknownType")
                textName += $" ({typeName})";

            _udonField.text = textName;

            UdonID = 0;

            _CheckSelected();
            _UpdateButtons();
        }



        // Custom functions.

        private object[] ListAdd(object[] list, object value)
        {
            object[] newArr = new object[list.Length + 1];

            list.CopyTo(newArr, 0);
            newArr[list.Length] = value;

            return newArr;
        }

        private object[] ListRemove(object[] list, int index)
        {
            if (index < 0 || index >= list.Length)
            {
                Debug.Log($"[<color=#00FF9F>NUDebugger</color>] Attempted to remove item at index: {index}. Array length was: {list.Length}");
                return list;
            }

            object[] newArr = new object[list.Length - 1];

            int j = 0;
            for (int i = 0; i < list.Length; i++)
            {
                if (i == index)
                    continue;

                newArr[j] = list[i];
                j += 1;
            }

            return newArr;
        }



        // Udon functions.

        private void _SetColor(GameObject target)
        {
            TextMeshProUGUI[] TMPs;
            UnityEngine.UI.Text[] Texts;
            UnityEngine.UI.Image[] Icons;

            TMPs = target.GetComponentsInChildren<TextMeshProUGUI>(true);
            Texts = target.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            Icons = target.GetComponentsInChildren<UnityEngine.UI.Image>(true);

            for (int i = 0; i < TMPs.Length; i++)
                TMPs[i].color = _mainColor;
            for (int i = 0; i < Texts.Length; i++)
                Texts[i].color = _mainColor;
            for (int i = 0; i < Icons.Length; i++)
                if (Icons[i].name.StartsWith("Icon-"))
                    Icons[i].color = _mainColor;
        }

        private void _RemoveUdon(int index)
        {
            Debug.LogError("[<color=#00FF9F>NUDebugger</color>] Removing missing UdonBehaviour from UdonDebugger.");

            object[] newArr;

            newArr = ListRemove(ArrUdons, index);
            ArrUdons = new Component[newArr.Length];
            newArr.CopyTo(ArrUdons, 0);

            newArr = ListRemove(ArrNames, index);
            ArrNames = new string[newArr.Length][];
            newArr.CopyTo(ArrNames, 0);

            newArr = ListRemove(VarNames, index);
            VarNames = new string[newArr.Length][];
            newArr.CopyTo(VarNames, 0);

            newArr = ListRemove(EntNames, index);
            EntNames = new string[newArr.Length][];
            newArr.CopyTo(EntNames, 0);

            UdonID = -1;
            MenuID = 0;

            _udonField.text = "[Removed]";
            _udonField.color = _crashColor;

            _UpdateButtons();
        }

        private void _DebugArray(UdonBehaviour udon, string name)
        {
            NUDebuggerText udonTarget = null;
            bool usePool = false;

            // Check if there are unused objects in the pool.
            for (int i = 0; i < _poolDebugText.Length;)
            {
                if (!Utilities.IsValid(_poolDebugText[i]))
                {
                    Debug.Log("[<color=#00FF9F>NUDebugger</color>] Removing missing pool object from pool array.");

                    object[] bufferArray = ListRemove(_poolDebugText, i);
                    _poolDebugText = new NUDebuggerText[bufferArray.Length];
                    bufferArray.CopyTo(_poolDebugText, 0);

                    continue;
                }

                if (!_poolDebugText[i].gameObject.activeSelf)
                {
                    udonTarget = _poolDebugText[i];
                    usePool = true;

                    Debug.Log($"[<color=#00FF9F>NUDebugger</color>] Found available pool object at index: {i}");

                    break;
                }

                i++;
            }

            // If all objects in the pool are being used then instantiate a new one.
            if (usePool)
            {
                udonTarget.transform.SetPositionAndRotation(_textTarget.position, _textTarget.rotation);

                udonTarget.gameObject.SetActive(true);
            }
            else
            { 
                GameObject newObject = VRCInstantiate(_textPrefab);

                newObject.transform.SetPositionAndRotation(_textTarget.position, _textTarget.rotation);
                newObject.transform.parent = _textContainer;

                // Store new UdonBehaviour into pool.
                udonTarget = newObject.GetComponent<NUDebuggerText>();

                object[] bufferArray = ListAdd(_poolDebugText, udonTarget);
                _poolDebugText = new NUDebuggerText[bufferArray.Length];
                bufferArray.CopyTo(_poolDebugText, 0);
            }

            // Set up behaviour.
            udonTarget.TargetUdon = udon;
            udonTarget.TargetName = name;
            udonTarget.TargetType = 0; // Array.
            udonTarget.transform.localScale = _targetAnimator.transform.localScale;

            udonTarget._Initialize();
        }

        private void _DebugVariable(UdonBehaviour udon, string name)
        {
            NUDebuggerText udonTarget = null;
            bool usePool = false;

            // Check if there are unused objects in the pool.
            for (int i = 0; i < _poolDebugText.Length;)
            {
                if (!Utilities.IsValid(_poolDebugText[i]))
                {
                    Debug.Log("[<color=#00FF9F>NUDebugger</color>] Removing missing pool object from pool array.");

                    object[] bufferArray = ListRemove(_poolDebugText, i);
                    _poolDebugText = new NUDebuggerText[bufferArray.Length];
                    bufferArray.CopyTo(_poolDebugText, 0);

                    continue;
                }

                if (!_poolDebugText[i].gameObject.activeSelf)
                {
                    udonTarget = _poolDebugText[i];
                    usePool = true;

                    Debug.Log($"[<color=#00FF9F>NUDebugger</color>] Found available pool object at index: {i}");

                    break;
                }

                i++;
            }

            // If all objects in the pool are being used then instantiate a new one.
            if (usePool)
            {
                udonTarget.transform.SetPositionAndRotation(_textTarget.position, _textTarget.rotation);

                udonTarget.gameObject.SetActive(true);
            }
            else
            { 
                GameObject newObject = VRCInstantiate(_textPrefab);

                newObject.transform.SetPositionAndRotation(_textTarget.position, _textTarget.rotation);
                newObject.transform.parent = _textContainer;

                // Store new UdonBehaviour into pool.
                udonTarget = newObject.GetComponent<NUDebuggerText>();

                object[] bufferArray = ListAdd(_poolDebugText, udonTarget);
                _poolDebugText = new NUDebuggerText[bufferArray.Length];
                bufferArray.CopyTo(_poolDebugText, 0);
            }

            // Set up behaviour.
            udonTarget.TargetUdon = udon;
            udonTarget.TargetName = name;
            udonTarget.TargetType = 1; // Variable.
            udonTarget.transform.localScale = _targetAnimator.transform.localScale;

            udonTarget._Initialize();
        }

        private void _DebugEvent(UdonBehaviour udon, string name)
        {
            if (_networked)
                udon.SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, name);
            else
                udon.SendCustomEvent(name);
        }
    }
}