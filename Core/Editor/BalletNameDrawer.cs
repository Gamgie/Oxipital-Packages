using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Oxipital
{
    /// <summary>The master ballet field: a dropdown of the dancer groups in the open scenes, None first.</summary>
    [CustomPropertyDrawer(typeof(BalletNameAttribute))]
    public class BalletNameDrawer : PropertyDrawer
    {
        static readonly List<string> Names = new List<string>();

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }
            Names.Clear();
            Names.Add("None");
            string self = (property.serializedObject.targetObject as Component)?.gameObject.name;
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            {
                if (!(behaviour is IDancerGroup)) continue;
                string n = behaviour.gameObject.name;
                if (n != self && !Names.Contains(n)) Names.Add(n);
            }
            // The groups a manager will create in Play mode ("Force Group 1"...), from its count
            AddManaged<OrbManager>("Orb Group", self);
            AddManaged<StandardForceManager>("Force Group", self);
            AddManaged<LightManager>("Light Group", self);
            string current = string.IsNullOrEmpty(property.stringValue) ? "None" : property.stringValue;
            if (!Names.Contains(current)) Names.Add(current);    // a group of a scene that is not open
            int index = Names.IndexOf(current);
            EditorGUI.BeginProperty(position, label, property);
            int chosen = EditorGUI.Popup(position, label.text, index, Names.ToArray());
            if (chosen != index) property.stringValue = Names[chosen];
            EditorGUI.EndProperty();
        }

        static void AddManaged<TManager>(string itemName, string self) where TManager : MonoBehaviour
        {
            foreach (var manager in Object.FindObjectsByType<TManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var count = new SerializedObject(manager).FindProperty("count");
                int n = Mathf.Max(1, Mathf.CeilToInt(count != null ? count.floatValue : 1f));
                for (int i = 1; i <= n; i++)
                {
                    string name = itemName + " " + i;
                    if (name != self && !Names.Contains(name)) Names.Add(name);
                }
            }
        }
    }
}
