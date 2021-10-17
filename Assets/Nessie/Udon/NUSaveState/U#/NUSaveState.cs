﻿
using System;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UdonSharp;

namespace Nessie.Udon.SaveState
{
    [AddComponentMenu("Udon Sharp/Nessie/NUSaveState")]
    public class NUSaveState : UdonSharpBehaviour
    {
        #region Serialized Public Fields

        public UdonBehaviour CallbackReciever;
        public string FallbackAvatarID = "avtr_c38a1615-5bf5-42b4-84eb-a8b6c37cbd11";

        #endregion Serialized Public Fields

        #region Serialized Private Fields

        // Instructions.
        [SerializeField] private int bufferByteCount;
        [SerializeField] private Component[] bufferUdonBehaviours;
        [SerializeField] private string[] bufferVariables;
        [SerializeField] private string[] bufferTypes;

        [SerializeField] private string[] dataAvatarIDs;
        [SerializeField] private Vector3[] dataKeyCoords;

        [SerializeField] private RuntimeAnimatorController[] parameterClearer; // Has to be an array to avoid an odd Udon serialization issue.
        [SerializeField] private RuntimeAnimatorController[] parameterWriters;

        #endregion Serialized Private Fields

        #region Private Fields

        private VRCPlayerApi localPlayer;

        private Transform pedestalContainer;
        private GameObject pedestalPrefab;

        private BoxCollider keyDetector;
        private VRCStation dataWriter;

        private VRC_AvatarPedestal[] dataAvatarPedestals;
        private VRC_AvatarPedestal fallbackAvatarPedestal;

        private byte[] inputBytes;
        private byte[] outputBytes;

        private object[] dataBones = new object[]
        {
            HumanBodyBones.LeftIndexDistal,
            HumanBodyBones.LeftMiddleDistal,
            HumanBodyBones.LeftRingDistal,
            HumanBodyBones.LeftLittleDistal,
            HumanBodyBones.RightIndexDistal,
            HumanBodyBones.RightMiddleDistal,
            HumanBodyBones.RightRingDistal,
            HumanBodyBones.RightLittleDistal,

            HumanBodyBones.LeftIndexIntermediate,
            HumanBodyBones.LeftMiddleIntermediate,
            HumanBodyBones.LeftRingIntermediate,
            HumanBodyBones.LeftLittleIntermediate,
            HumanBodyBones.RightIndexIntermediate,
            HumanBodyBones.RightMiddleIntermediate,
            HumanBodyBones.RightRingIntermediate,
            HumanBodyBones.RightLittleIntermediate,

            HumanBodyBones.LeftIndexProximal,
            HumanBodyBones.LeftMiddleProximal,
            HumanBodyBones.LeftRingProximal,
            HumanBodyBones.LeftLittleProximal,
            HumanBodyBones.RightIndexProximal,
            HumanBodyBones.RightMiddleProximal,
            HumanBodyBones.RightRingProximal,
            HumanBodyBones.RightLittleProximal,
        };
        private float dataMinRange = 0.0009971f;
        private float dataMaxRange = 0.4999345f;

        private string[] callbackEvents = new string[]
        {
            "_SSSaved",
            "_SSLoaded",
            "_SSSaveFailed",
            "_SSLoadFailed",
            "_SSPostSave",
            "_SSPostLoad"
        };
        private bool avatarIsLoading;
        private int dataStatus = 0;

        private float dataAvatarTimeout;
        private int dataAvatarIndex;
        private int dataByteIndex;
        private int dataBitIndex;

        #endregion Private Fields

        #region Unity Events

        private void Start()
        {
            if (gameObject.layer != 5)
                Debug.LogWarning("[<color=#00FF9F>SaveState</color>] NUSaveState behaviour is not situated on the UI layer, this might prevent the behaviour from detecting data avatars.");

            localPlayer = Networking.LocalPlayer;

            // Workaround for prefab serialization issues. Angy. >:(
            // (Though please do tell if you happen to know a better way around this. - Nestorboy#7647)
            pedestalContainer = transform.GetChild(0);
            pedestalPrefab = pedestalContainer.GetChild(0).gameObject;

            // Prevent other people from being moved over to the location of the SaveState, moving them to the world origin instead.
            transform.name = $"{localPlayer.displayName} {localPlayer.GetHashCode()}";

            inputBytes = new byte[bufferByteCount];
            outputBytes = new byte[bufferByteCount];

            keyDetector = GetComponent<BoxCollider>();

            // Prepare data avatar pedestals.
            dataAvatarPedestals = new VRC_AvatarPedestal[dataAvatarIDs.Length];
            for (int i = 0; i < dataAvatarIDs.Length; i++)
            {
                dataAvatarPedestals[i] = (VRC_AvatarPedestal)VRCInstantiate(pedestalPrefab).GetComponent(typeof(VRC_AvatarPedestal));
                dataAvatarPedestals[i].transform.SetParent(pedestalContainer, false);
                dataAvatarPedestals[i].transform.localPosition = new Vector3(0, i + 1, 0);
                dataAvatarPedestals[i].blueprintId = dataAvatarIDs[i];
            }

            // Prepare fallback avatar pedestal.
            fallbackAvatarPedestal = (VRC_AvatarPedestal)pedestalPrefab.GetComponent(typeof(VRC_AvatarPedestal));
            fallbackAvatarPedestal.transform.SetParent(pedestalContainer, false);
            fallbackAvatarPedestal.transform.localPosition = new Vector3(1, 1, 0);
            fallbackAvatarPedestal.blueprintId = FallbackAvatarID;

            // Get VRCStation due to reference being broken when set up through inspector.
            dataWriter = (VRCStation)GetComponent(typeof(VRCStation));
        }

        private void OnParticleCollision(GameObject other)
        {
            if (avatarIsLoading)
            {
                Debug.Log($"[<color=#00FF9F>SaveState</color>] Detected buffer avatar: {dataAvatarIndex}.");

                dataAvatarTimeout = 0;
                avatarIsLoading = false;
                keyDetector.enabled = false;

                if (dataStatus == 1)
                    _ClearData();
                else
                    _GetData();
            }
            else if (dataStatus > 4)
            {
                dataAvatarTimeout = 2f;
            }
        }

        #endregion Unity Events

        #region SaveState API

        public void _SSSave()
        {
            if (dataStatus > 0 && dataStatus < 3) return;
            dataStatus = 1;

            _PackData();

            dataAvatarIndex = 0;
            _ChangeAvatar();
        }

        public void _SSLoad()
        {
            if (dataStatus > 0 && dataStatus < 3) return;
            dataStatus = 2;

            dataAvatarIndex = 0;
            _ChangeAvatar();
        }

        private void _SSHook()
        {
            CallbackReciever.SendCustomEvent(callbackEvents[dataStatus - 1]);
        }

        #endregion SaveState API

        #region SaveState Data

        private void _ChangeAvatar()
        {
            dataBitIndex = 0;
            dataByteIndex = 0;

            Debug.Log($"[<color=#00FF9F>SaveState</color>] Switching avatar to buffer avatar: {dataAvatarIndex}.");
            dataAvatarPedestals[dataAvatarIndex].SetAvatarUse(localPlayer);

            dataAvatarTimeout = 30;
            avatarIsLoading = true;
            keyDetector.enabled = true;
            _LookForAvatar();
        }

        public void _LookForAvatar()
        {
            keyDetector.center = transform.InverseTransformPoint(localPlayer.GetRotation() * dataKeyCoords[dataAvatarIndex] + localPlayer.GetPosition());

            if (avatarIsLoading)
            {
                if (dataAvatarTimeout > 0)
                {
                    dataAvatarTimeout -= Time.deltaTime;
                    SendCustomEventDelayedFrames(nameof(_LookForAvatar), 1);
                }
                else
                {
                    Debug.LogError("[<color=#00FF9F>SaveState</color>] Data avatar took too long to load or avatar ID is mismatched.");
                    _FailedData();
                }
            }
            else if (dataStatus > 4)
            {
                if (dataAvatarTimeout > 0)
                {
                    dataAvatarTimeout -= Time.deltaTime;
                    SendCustomEventDelayedFrames(nameof(_LookForAvatar), 1);
                }
                else
                {
                    _SSHook();
                    dataStatus = 0;
                }
            }
        }

        private void _ClearData() // Reset all the parameters before writing to them.
        {
            transform.SetPositionAndRotation(localPlayer.GetPosition(), localPlayer.GetRotation());
            dataWriter.animatorController = parameterClearer[0];
            localPlayer.UseAttachedStation();

            dataBitIndex = 0;
            SendCustomEventDelayedFrames(nameof(_SetData), 2);
        }

        public void _SetData() // Write data by doing float additions.
        {
            dataWriter.ExitStation(localPlayer);

            if (dataBitIndex < 8)
            {
                if (((inputBytes[dataByteIndex + dataAvatarIndex * 32] >> dataBitIndex) & 1) == 1)
                {
                    transform.SetPositionAndRotation(localPlayer.GetPosition(), localPlayer.GetRotation());
                    dataWriter.animatorController = parameterWriters[dataBitIndex + dataByteIndex * 8];
                    localPlayer.UseAttachedStation();

                    dataBitIndex++;
                    SendCustomEventDelayedFrames(nameof(_SetData), 1);
                }
                else
                {
                    dataBitIndex++;
                    _SetData();
                }
            }
            else
            {
                dataBitIndex = 0;
                dataByteIndex++;

                if (dataByteIndex + dataAvatarIndex * 32 >= bufferByteCount)
                {
                    _FinishedData();
                }
                else
                {
                    if (dataByteIndex < 32)
                    {
                        _SetData();
                    }
                    else
                    {
                        dataAvatarIndex++;
                        _ChangeAvatar();
                    }
                }
            }
        }

        private void _GetData() // Read data using finger rotations.
        {
            int avatarByteCount = Mathf.Min(bufferByteCount - dataAvatarIndex * 32, 32);
            for (int boneIndex = 0; dataByteIndex < avatarByteCount; boneIndex++)
            {
                Quaternion muscleTarget = localPlayer.GetBoneRotation((HumanBodyBones)dataBones[boneIndex]);
                Quaternion muscleParent = localPlayer.GetBoneRotation((HumanBodyBones)dataBones[boneIndex + 8]);

                int bytes = Mathf.RoundToInt(InverseMuscle(muscleTarget, muscleParent) * 65536) & 0xFFFF; // 2 bytes per parameter.
                outputBytes[dataByteIndex++ + dataAvatarIndex * 32] = (byte)(bytes & 0xFF);
                if (dataByteIndex < avatarByteCount)
                    outputBytes[dataByteIndex++ + dataAvatarIndex * 32] = (byte)(bytes >> 8);
            }

            if (dataByteIndex + dataAvatarIndex * 32 < bufferByteCount)
            {
                dataAvatarIndex++;
                _ChangeAvatar();
            }
            else
            {
                _FinishedData();
            }
        }

        private void _FinishedData()
        {
            if (dataStatus == 1)
            {
                Debug.Log($"[<color=#00FF9F>SaveState</color>] Data has been saved.");
            }
            else
            {
                _UnpackData();

                Debug.Log("[<color=#00FF9F>SaveState</color>] Data has been loaded.");
            }

            _SSHook();

            fallbackAvatarPedestal.SetAvatarUse(localPlayer);

            dataAvatarTimeout = 30;
            dataStatus += 4;
            keyDetector.enabled = true;
            _LookForAvatar();
        }

        private void _FailedData()
        {
            avatarIsLoading = false;
            keyDetector.enabled = false;

            dataStatus += 2;
            _SSHook();
            dataStatus = 0;
        }

        private void _PackData()
        {
            int bufferLength = 0;
            byte[] bufferBytes = __PrepareWriteBuffer();
            for (int i = 0; bufferLength < bufferBytes.Length; i++)
            {
                if (bufferUdonBehaviours[i] == null || bufferVariables[i] == null) return;

                object value = ((UdonBehaviour)bufferUdonBehaviours[i]).GetProgramVariable(bufferVariables[i]);
                bufferLength = __WriteBufferTypedObject(value, bufferTypes[i], bufferBytes, bufferLength);
            }

            inputBytes = __FinalizeWriteBuffer(bufferBytes, bufferLength);
        }

        private void _UnpackData()
        {
            byte[] bufferBytes = __PrepareReadBuffer(outputBytes);
            for (int i = 0; _currentBufferIndex < bufferBytes.Length; i++)
            {
                if (bufferUdonBehaviours[i] == null || bufferVariables[i] == null) return;

                object value = __ReadBufferTypedObject(bufferTypes[i], bufferBytes);
                ((UdonBehaviour)bufferUdonBehaviours[i]).SetProgramVariable(bufferVariables[i], value);
            }
        }

        private float InverseMuscle(Quaternion a, Quaternion b) // Funky numbers that make the world go round.
        {
            Quaternion deltaQ = Quaternion.Inverse(b) * a;
            float initialRange = Mathf.Abs(Mathf.Asin(deltaQ.x)) * 4 / Mathf.PI;
            float normalizedRange = (initialRange - dataMinRange) / dataMaxRange;

            return normalizedRange;
        }

        #endregion SaveState Data

        #region Buffer Utilities

        // Thank you Genesis for the bit converter code!
        // https://gist.github.com/NGenesis/4dbc68228f8abff292812dfff0638c47 (MIT-License)

        private int _currentBufferIndex = 0;
        private byte[] _tempBuffer = null;

        private byte[] __PrepareWriteBuffer()
        {
            if (_tempBuffer == null) _tempBuffer = new byte[bufferByteCount];
            return _tempBuffer;
        }

        private byte[] __FinalizeWriteBuffer(byte[] buffer, int length)
        {
            var finalBuffer = new byte[length];
            Array.Copy(buffer, finalBuffer, length);
            return finalBuffer;
        }

        private byte[] __PrepareReadBuffer(byte[] buffer)
        {
            _currentBufferIndex = 0;
            return buffer;
        }

        private int __WriteBufferTypedObject(object value, string typeName, byte[] buffer, int index)
        {
            if (typeName == typeof(int).FullName) return __WriteBufferInteger((int)(value ?? 0), buffer, index);
            else if (typeName == typeof(uint).FullName) return __WriteBufferUnsignedInteger((uint)(value ?? 0U), buffer, index);
            else if (typeName == typeof(long).FullName) return __WriteBufferLong((long)(value ?? 0L), buffer, index);
            else if (typeName == typeof(ulong).FullName) return __WriteBufferUnsignedLong((ulong)(value ?? 0UL), buffer, index);
            else if (typeName == typeof(short).FullName) return __WriteBufferShort((short)(value ?? 0), buffer, index);
            else if (typeName == typeof(ushort).FullName) return __WriteBufferUnsignedShort((ushort)(value ?? 0U), buffer, index);
            else if (typeName == typeof(byte).FullName) return __WriteBufferByte((byte)(value ?? 0), buffer, index);
            else if (typeName == typeof(sbyte).FullName) return __WriteBufferSignedByte((sbyte)(value ?? 0), buffer, index);
            else if (typeName == typeof(char).FullName) return __WriteBufferChar((char)(value ?? 0), buffer, index);
            else if (typeName == typeof(float).FullName) return __WriteBufferFloat((float)(value ?? 0.0f), buffer, index);
            else if (typeName == typeof(double).FullName) return __WriteBufferDouble((double)(value ?? 0.0), buffer, index);
            else if (typeName == typeof(decimal).FullName) return __WriteBufferDecimal((decimal)(value ?? 0m), buffer, index);
            else if (typeName == typeof(bool).FullName) return __WriteBufferBoolean((bool)(value ?? false), buffer, index);
            else if (typeName == typeof(Vector2).FullName) return __WriteBufferVector2((Vector2)(value ?? Vector2.zero), buffer, index);
            else if (typeName == typeof(Vector3).FullName) return __WriteBufferVector3((Vector3)(value ?? Vector3.zero), buffer, index);
            else if (typeName == typeof(Vector4).FullName) return __WriteBufferVector4((Vector4)(value ?? Vector4.zero), buffer, index);
            /* UI 1.5 Update
            else if (type == typeof(Vector2Int).FullName) return __WriteBufferVector2Int((Vector2Int)(value ?? Vector2Int.zero), buffer, index);
            else if (type == typeof(Vector3Int).FullName) return __WriteBufferVector3Int((Vector3Int)(value ?? Vector3Int.zero), buffer, index);
            */
            else if (typeName == typeof(Quaternion).FullName) return __WriteBufferQuaternion((value != null ? (Quaternion)value : Quaternion.identity), buffer, index);
            else if (typeName == typeof(Color).FullName) return __WriteBufferColor((value != null ? (Color)value : Color.clear), buffer, index);
            else if (typeName == typeof(Color32).FullName) return __WriteBufferColor32((value != null ? (Color32)value : (Color32)Color.clear), buffer, index);
            else if (typeName == typeof(VRCPlayerApi).FullName) return __WriteBufferVRCPlayer((VRCPlayerApi)value, buffer, index);
            return index;
        }

        private object __ReadBufferTypedObject(string typeName, byte[] buffer)
        {
            if (typeName == typeof(int).FullName) return __ReadBufferInteger(buffer);
            else if (typeName == typeof(uint).FullName) return __ReadBufferUnsignedInteger(buffer);
            else if (typeName == typeof(long).FullName) return __ReadBufferLong(buffer);
            else if (typeName == typeof(ulong).FullName) return __ReadBufferUnsignedLong(buffer);
            else if (typeName == typeof(short).FullName) return __ReadBufferShort(buffer);
            else if (typeName == typeof(ushort).FullName) return __ReadBufferUnsignedShort(buffer);
            else if (typeName == typeof(byte).FullName) return __ReadBufferByte(buffer);
            else if (typeName == typeof(sbyte).FullName) return __ReadBufferSignedByte(buffer);
            else if (typeName == typeof(char).FullName) return __ReadBufferChar(buffer);
            else if (typeName == typeof(float).FullName) return __ReadBufferFloat(buffer);
            else if (typeName == typeof(double).FullName) return __ReadBufferDouble(buffer);
            else if (typeName == typeof(decimal).FullName) return __ReadBufferDecimal(buffer);
            else if (typeName == typeof(bool).FullName) return __ReadBufferBoolean(buffer);
            else if (typeName == typeof(Vector2).FullName) return __ReadBufferVector2(buffer);
            else if (typeName == typeof(Vector3).FullName) return __ReadBufferVector3(buffer);
            else if (typeName == typeof(Vector4).FullName) return __ReadBufferVector4(buffer);
            /* UI 1.5 Update
            else if (type == typeof(Vector2Int).FullName) return __ReadBufferVector2Int(buffer);
            else if (type == typeof(Vector3Int).FullName) return __ReadBufferVector3Int(buffer);
            */
            else if (typeName == typeof(Quaternion).FullName) return __ReadBufferQuaternion(buffer);
            else if (typeName == typeof(Color).FullName) return __ReadBufferColor(buffer);
            else if (typeName == typeof(Color32).FullName) return __ReadBufferColor32(buffer);
            else if (typeName == typeof(VRCPlayerApi).FullName) return __ReadBufferVRCPlayer(buffer);
            return null;
        }

        private bool __ReadBufferBoolean(byte[] buffer) => buffer[_currentBufferIndex++] != 0;
        private int __WriteBufferBoolean(bool value, byte[] buffer, int index)
        {
            buffer[index++] = value ? (byte)1 : (byte)0;
            return index;
        }

        private char __ReadBufferChar(byte[] buffer) => (char)__ReadBufferShort(buffer);
        private int __WriteBufferChar(char value, byte[] buffer, int index) => __WriteBufferShort((short)value, buffer, index);

        private byte __ReadBufferByte(byte[] buffer) => buffer[_currentBufferIndex++];
        private int __WriteBufferByte(byte value, byte[] buffer, int index)
        {
            buffer[index++] = value;
            return index;
        }

        private sbyte __ReadBufferSignedByte(byte[] buffer)
        {
            int value = buffer[_currentBufferIndex++];
            if (value > 0x80) value = value - 0xFF;
            return Convert.ToSByte(value);
        }
        private int __WriteBufferSignedByte(sbyte value, byte[] buffer, int index)
        {
            buffer[index++] = (byte)(value < 0 ? (value + 0xFF) : value);
            return index;
        }

        private short __ReadBufferShort(byte[] buffer)
        {
            int value = buffer[_currentBufferIndex++] << 8 | buffer[_currentBufferIndex++];
            if (value > 0x8000) value = value - 0xFFFF;
            return Convert.ToInt16(value);
        }
        private int __WriteBufferShort(short value, byte[] buffer, int index)
        {
            int tmp = value < 0 ? (value + 0xFFFF) : value;
            buffer[index++] = (byte)(tmp >> 8);
            buffer[index++] = (byte)(tmp & 0xFF);
            return index;
        }

        private ushort __ReadBufferUnsignedShort(byte[] buffer) => Convert.ToUInt16(buffer[_currentBufferIndex++] << 8 | buffer[_currentBufferIndex++]);
        private int __WriteBufferUnsignedShort(ushort value, byte[] buffer, int index)
        {
            int tmp = Convert.ToInt32(value);
            buffer[index++] = (byte)(tmp >> 8);
            buffer[index++] = (byte)(tmp & 0xFF);
            return index;
        }

        private int __ReadBufferInteger(byte[] buffer) => ((int)buffer[_currentBufferIndex++] << 24) | ((int)buffer[_currentBufferIndex++] << 16) | ((int)buffer[_currentBufferIndex++] << 8) | ((int)buffer[_currentBufferIndex++]);
        private int __WriteBufferInteger(int value, byte[] buffer, int index)
        {
            buffer[index++] = (byte)((value >> 24) & 0xFF);
            buffer[index++] = (byte)((value >> 16) & 0xFF);
            buffer[index++] = (byte)((value >> 8) & 0xFF);
            buffer[index++] = (byte)(value & 0xFF);
            return index;
        }

        private uint __ReadBufferUnsignedInteger(byte[] buffer) => ((uint)buffer[_currentBufferIndex++] << 24) | ((uint)buffer[_currentBufferIndex++] << 16) | ((uint)buffer[_currentBufferIndex++] << 8) | ((uint)buffer[_currentBufferIndex++]);
        private int __WriteBufferUnsignedInteger(uint value, byte[] buffer, int index)
        {
            buffer[index++] = (byte)((value >> 24) & 255u);
            buffer[index++] = (byte)((value >> 16) & 255u);
            buffer[index++] = (byte)((value >> 8) & 255u);
            buffer[index++] = (byte)(value & 255u);
            return index;
        }

        private long __ReadBufferLong(byte[] buffer) => ((long)buffer[_currentBufferIndex++] << 56) | ((long)buffer[_currentBufferIndex++] << 48) | ((long)buffer[_currentBufferIndex++] << 40) | ((long)buffer[_currentBufferIndex++] << 32) | ((long)buffer[_currentBufferIndex++] << 24) | ((long)buffer[_currentBufferIndex++] << 16) | ((long)buffer[_currentBufferIndex++] << 8) | ((long)buffer[_currentBufferIndex++]);
        private int __WriteBufferLong(long value, byte[] buffer, int index)
        {
            buffer[index++] = (byte)((value >> 56) & 0xFF);
            buffer[index++] = (byte)((value >> 48) & 0xFF);
            buffer[index++] = (byte)((value >> 40) & 0xFF);
            buffer[index++] = (byte)((value >> 32) & 0xFF);
            buffer[index++] = (byte)((value >> 24) & 0xFF);
            buffer[index++] = (byte)((value >> 16) & 0xFF);
            buffer[index++] = (byte)((value >> 8) & 0xFF);
            buffer[index++] = (byte)(value & 0xFF);
            return index;
        }

        private ulong __ReadBufferUnsignedLong(byte[] buffer) => ((ulong)buffer[_currentBufferIndex++] << 56) | ((ulong)buffer[_currentBufferIndex++] << 48) | ((ulong)buffer[_currentBufferIndex++] << 40) | ((ulong)buffer[_currentBufferIndex++] << 32) | ((ulong)buffer[_currentBufferIndex++] << 24) | ((ulong)buffer[_currentBufferIndex++] << 16) | ((ulong)buffer[_currentBufferIndex++] << 8) | ((ulong)buffer[_currentBufferIndex++]);
        private int __WriteBufferUnsignedLong(ulong value, byte[] buffer, int index)
        {
            buffer[index++] = (byte)((value >> 56) & 255ul);
            buffer[index++] = (byte)((value >> 48) & 255ul);
            buffer[index++] = (byte)((value >> 40) & 255ul);
            buffer[index++] = (byte)((value >> 32) & 255ul);
            buffer[index++] = (byte)((value >> 24) & 255ul);
            buffer[index++] = (byte)((value >> 16) & 255ul);
            buffer[index++] = (byte)((value >> 8) & 255ul);
            buffer[index++] = (byte)(value & 255ul);
            return index;
        }

        private decimal __ReadBufferDecimal(byte[] buffer)
        {
            int signScaleBits = __ReadBufferInteger(buffer);
            return new Decimal(__ReadBufferInteger(buffer), __ReadBufferInteger(buffer), __ReadBufferInteger(buffer), (signScaleBits & 0x80000000) != 0, (byte)((signScaleBits >> 16) & 127));
        }
        private int __WriteBufferDecimal(decimal value, byte[] buffer, int index)
        {
            int[] bits = Decimal.GetBits(value);
            index = __WriteBufferInteger(bits[3], buffer, index); // Sign & scale bits
            index = __WriteBufferInteger(bits[0], buffer, index);
            index = __WriteBufferInteger(bits[1], buffer, index);
            index = __WriteBufferInteger(bits[2], buffer, index);
            return index;
        }

        private Vector2 __ReadBufferVector2(byte[] buffer) => new Vector2(__ReadBufferFloat(buffer), __ReadBufferFloat(buffer));
        private int __WriteBufferVector2(Vector2 value, byte[] buffer, int index)
        {
            index = __WriteBufferFloat(value.x, buffer, index);
            index = __WriteBufferFloat(value.y, buffer, index);
            return index;
        }

        private Vector3 __ReadBufferVector3(byte[] buffer) => new Vector3(__ReadBufferFloat(buffer), __ReadBufferFloat(buffer), __ReadBufferFloat(buffer));
        private int __WriteBufferVector3(Vector3 value, byte[] buffer, int index)
        {
            index = __WriteBufferFloat(value.x, buffer, index);
            index = __WriteBufferFloat(value.y, buffer, index);
            index = __WriteBufferFloat(value.z, buffer, index);
            return index;
        }

        private Vector4 __ReadBufferVector4(byte[] buffer) => new Vector4(__ReadBufferFloat(buffer), __ReadBufferFloat(buffer), __ReadBufferFloat(buffer), __ReadBufferFloat(buffer));
        private int __WriteBufferVector4(Vector4 value, byte[] buffer, int index)
        {
            index = __WriteBufferFloat(value.x, buffer, index);
            index = __WriteBufferFloat(value.y, buffer, index);
            index = __WriteBufferFloat(value.z, buffer, index);
            index = __WriteBufferFloat(value.w, buffer, index);
            return index;
        }

        /* UI 1.5 Update
        private Vector2Int __ReadBufferVector2Int(byte[] buffer) => new Vector2Int(__ReadBufferInteger(buffer), __ReadBufferInteger(buffer));
        private int __WriteBufferVector2Int(Vector2Int value, byte[] buffer, int index)
        {
            index = __WriteBufferInteger(value.x, buffer, index);
            index = __WriteBufferInteger(value.y, buffer, index);
            return index;
        }

        private Vector3Int __ReadBufferVector3Int(byte[] buffer) => new Vector3Int(__ReadBufferInteger(buffer), __ReadBufferInteger(buffer), __ReadBufferInteger(buffer));
        private int __WriteBufferVector3Int(Vector3Int value, byte[] buffer, int index)
        {
            index = __WriteBufferInteger(value.x, buffer, index);
            index = __WriteBufferInteger(value.y, buffer, index);
            index = __WriteBufferInteger(value.z, buffer, index);
            return index;
        }
        */

        private Quaternion __ReadBufferQuaternion(byte[] buffer) => new Quaternion(__ReadBufferFloat(buffer), __ReadBufferFloat(buffer), __ReadBufferFloat(buffer), __ReadBufferFloat(buffer));
        private int __WriteBufferQuaternion(Quaternion value, byte[] buffer, int index)
        {
            index = __WriteBufferFloat(value.x, buffer, index);
            index = __WriteBufferFloat(value.y, buffer, index);
            index = __WriteBufferFloat(value.z, buffer, index);
            index = __WriteBufferFloat(value.w, buffer, index);
            return index;
        }

        private Color __ReadBufferColor(byte[] buffer) => new Color(__ReadBufferFloat(buffer), __ReadBufferFloat(buffer), __ReadBufferFloat(buffer), __ReadBufferFloat(buffer));
        private int __WriteBufferColor(Color value, byte[] buffer, int index)
        {
            index = __WriteBufferFloat(value.r, buffer, index);
            index = __WriteBufferFloat(value.g, buffer, index);
            index = __WriteBufferFloat(value.b, buffer, index);
            index = __WriteBufferFloat(value.a, buffer, index);
            return index;
        }

        private Color32 __ReadBufferColor32(byte[] buffer) => new Color32(__ReadBufferByte(buffer), __ReadBufferByte(buffer), __ReadBufferByte(buffer), __ReadBufferByte(buffer));
        private int __WriteBufferColor32(Color32 value, byte[] buffer, int index)
        {
            index = __WriteBufferByte(value.r, buffer, index);
            index = __WriteBufferByte(value.g, buffer, index);
            index = __WriteBufferByte(value.b, buffer, index);
            index = __WriteBufferByte(value.a, buffer, index);
            return index;
        }

        private VRCPlayerApi __ReadBufferVRCPlayer(byte[] buffer) => VRCPlayerApi.GetPlayerById(__ReadBufferInteger(buffer));
        private int __WriteBufferVRCPlayer(VRCPlayerApi value, byte[] buffer, int index) => __WriteBufferInteger(Utilities.IsValid(value) ? value.playerId : -1, buffer, index);

        private const uint _FLOAT_SIGN_BIT = 0x80000000;
        private const uint _FLOAT_EXP_MASK = 0x7F800000;
        private const uint _FLOAT_FRAC_MASK = 0x007FFFFF;

        private float __ReadBufferFloat(byte[] buffer)
        {
            uint value = __ReadBufferUnsignedInteger(buffer);
            if (value == 0 || value == _FLOAT_SIGN_BIT) return 0.0f;

            int exp = (int)((value & _FLOAT_EXP_MASK) >> 23);
            int frac = (int)(value & _FLOAT_FRAC_MASK);
            bool negate = (value & _FLOAT_SIGN_BIT) == _FLOAT_SIGN_BIT;

            if (exp == 0xFF)
            {
                if (frac == 0) return negate ? float.NegativeInfinity : float.PositiveInfinity;
                return float.NaN;
            }

            bool normal = exp != 0x00;
            if (normal) exp -= 127;
            else exp = -126;

            float result = frac / (float)(2 << 22);
            if (normal) result += 1.0f;

            result *= Mathf.Pow(2, exp);
            if (negate) result = -result;

            return result;
        }
        private int __WriteBufferFloat(float value, byte[] buffer, int index)
        {
            uint tmp = 0;
            if (float.IsNaN(value))
            {
                tmp = _FLOAT_EXP_MASK | _FLOAT_FRAC_MASK;
            }
            else if (float.IsInfinity(value))
            {
                tmp = _FLOAT_EXP_MASK;
                if (float.IsNegativeInfinity(value)) tmp |= _FLOAT_SIGN_BIT;
            }
            else if (value != 0.0f)
            {
                if (value < 0.0f)
                {
                    value = -value;
                    tmp |= _FLOAT_SIGN_BIT;
                }

                int exp = 0;
                while (value >= 2.0f)
                {
                    value *= 0.5f;
                    ++exp;
                }

                bool normal = true;
                while (value < 1.0f)
                {
                    if (exp == -126)
                    {
                        normal = false;
                        break;
                    }

                    value *= 2.0f;
                    --exp;
                }

                if (normal)
                {
                    value -= 1.0f;
                    exp += 127;
                }
                else exp = 0;

                tmp |= Convert.ToUInt32(exp << 23) & _FLOAT_EXP_MASK;
                tmp |= Convert.ToUInt32(value * (2 << 22)) & _FLOAT_FRAC_MASK;
            }

            return __WriteBufferUnsignedInteger(tmp, buffer, index);
        }

        private double __ReadBufferDouble(byte[] buffer)
        {
            return double.Parse(__ReadBufferStringAscii(buffer));
        }
        private int __WriteBufferDouble(double value, byte[] buffer, int index)
        {
            return __WriteBufferStringAscii(value.ToString("R"), buffer, index);
        }
        /*
            private const ulong _DOUBLE_SIGN_BIT = 0x8000000000000000;
            private const ulong _DOUBLE_EXP_MASK = 0x7FF0000000000000;
            private const ulong _DOUBLE_FRAC_MASK = 0x000FFFFFFFFFFFFF;
            private double __ReadBufferDouble(byte[] buffer) {
                ulong value = __ReadBufferUnsignedLong(buffer);
                if(value == 0 || value == _DOUBLE_SIGN_BIT) return 0.0;
                long exp = (long)((value & _DOUBLE_EXP_MASK) >> 52);
                long frac = (long)(value & _DOUBLE_FRAC_MASK);
                bool negate = (value & _DOUBLE_SIGN_BIT) == _DOUBLE_SIGN_BIT;
                if(exp == 0x7FF) {
                    if(frac == 0) return negate ? double.NegativeInfinity : double.PositiveInfinity;
                    return double.NaN;
                }
                bool normal = exp != 0x000;
                if(normal) exp -= 1023;
                else exp = -1022;
                double result = frac / (double)(2 << 51);
                if(normal) result += 1.0;
                result *= Math.Pow(2, exp); // Math.Pow not exposed in Udon!
                if(negate) result = -result;
                return result;
            }
            private int __WriteBufferDouble(double value, byte[] buffer, int index) {
                ulong tmp = 0;
                if(double.IsNaN(value)) {
                    tmp = _DOUBLE_EXP_MASK | _DOUBLE_FRAC_MASK;
                } else if(double.IsInfinity(value)) {
                    tmp = _DOUBLE_EXP_MASK;
                    if(double.IsNegativeInfinity(value)) tmp |= _DOUBLE_SIGN_BIT;
                } else if(value != 0.0) {
                    if(value < 0.0) {
                        value = -value;
                        tmp |= _DOUBLE_SIGN_BIT;
                    }
                    long exp = 0;
                    while(value >= 2.0) {
                        value *= 0.5;
                        ++exp;
                    }
                    bool normal = true;
                    while(value < 1.0) {
                        if(exp == -1022) {
                            normal = false;
                            break;
                        }
                        value *= 2.0;
                        --exp;
                    }
                    if(normal) {
                        value -= 1.0;
                        exp += 1023;
                    } else exp = 0;
                    tmp |= Convert.ToUInt64(exp << 23) & _DOUBLE_EXP_MASK;
                    tmp |= Convert.ToUInt64(value * (2 << 22)) & _DOUBLE_FRAC_MASK;
                }
                return __WriteBufferUnsignedLong(tmp, buffer, index);
            }
        */

        private string __ReadBufferStringAscii(byte[] buffer)
        {
            int bytesCount = __ReadBufferInteger(buffer);

            char[] chars = new char[bytesCount];
            for (var i = 0; i < bytesCount; ++i) chars[i] = Convert.ToChar(buffer[_currentBufferIndex++] & 0x7F);
            return new string(chars);
        }
        private int __WriteBufferStringAscii(string str, byte[] buffer, int index)
        {
            index = __WriteBufferInteger(str.Length, buffer, index);
            for (var i = 0; i < str.Length; ++i) buffer[index++] = (byte)(str[i] & 0x7F);
            return index;
        }

        #endregion Buffer Utilities
    }
}