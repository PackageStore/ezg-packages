using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityFigmaBridge.Runtime.UI;

namespace UnityFigmaBridge.Editor.PrototypeFlow
{
    public static class BehaviourBindingManager
    {


        private const int MAX_SEARCH_DEPTH_FOR_TRANSFORMS = 3;
        
        /// <summary>
        /// Attempts to find a suitable mono behaviour to bind. Returns true when the node was changed
        /// </summary>
        /// <param name="gameObject"></param>
        /// <param name="importProcessData"></param>
        /// <param name="behaviourTypesByName">Lookup from <see cref="BuildBehaviourTypeLookup"/></param>
        private static bool BindBehaviourToNode(GameObject gameObject, FigmaImportProcessData importProcessData,
            Dictionary<string, List<Type>> behaviourTypesByName)
        {
            // Add in any special behaviours driven by name or other rules. If special case, dont add any more behaviours
            bool specialCaseNode=AddSpecialBehavioursToNode(gameObject,importProcessData);
            if (specialCaseNode) return true;

            if (!behaviourTypesByName.TryGetValue(gameObject.name, out var candidateTypes)) return false;
            var matchingType = SelectType(candidateTypes, importProcessData.Settings.ScreenBindingNamespace);
            if (matchingType == null) return false;

            // Make sure it doesnt already have this component attached (this can happen for nested components)
            var behaviourAdded = false;
            var attachedBehaviour = gameObject.GetComponent(matchingType);
            if (attachedBehaviour == null)
            {
                attachedBehaviour = gameObject.AddComponent(matchingType);
                // AddComponent logs and returns null when the type cannot go on this object (e.g. a second Graphic)
                if (attachedBehaviour == null) return false;
                behaviourAdded = true;
            }

            // Find all fields for this class, and if inherit from component, look to assign
            return BindFieldsForComponent(gameObject, attachedBehaviour) || behaviourAdded;
        }

        private static bool AddSpecialBehavioursToNode(GameObject gameObject, FigmaImportProcessData importProcessData)
        {
            if (gameObject.name.ToUpper() == "SAFEAREA")
            {
                // Add in a safe area component for correct resizing
                if (gameObject.GetComponent<SafeArea>() == null)
                {
                    gameObject.AddComponent<SafeArea>();
                    // Also move pivot to top left, to make offset calc a bit easier
                    //FigmaDocumentUtils.SetPivot(gameObject.transform as RectTransform, new Vector2(0,1));
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Assigns serialized fields and [BindFigmaButtonPress] methods of a component from child nodes with matching names.
        /// Returns true when a field got a new value or a button listener was added
        /// </summary>
        public static bool BindFieldsForComponent(GameObject gameObject, Component component)
        {
            var changed = false;
            var componentType = component.GetType();
            
            // Then check private fields
            FieldInfo[] privateSerializedFields=componentType.GetFields(
                BindingFlags.NonPublic | 
                BindingFlags.Instance);
            List<FieldInfo> allSerializedComponentFields = privateSerializedFields.Where(field => field.GetCustomAttribute(typeof(SerializeField)) != null).ToList();
            
            // And add all public fields
            allSerializedComponentFields.AddRange(componentType.GetFields());
            
            foreach (var field in allSerializedComponentFields)
            {
                var fieldType = field.FieldType;
                // See if there is a child transform with matching name (case insensitive)
                var matchingTransform = GetChildTransformByName(gameObject.transform, field.Name, true,MAX_SEARCH_DEPTH_FOR_TRANSFORMS);
                if (matchingTransform)
                {
                    if (fieldType == typeof(GameObject))
                    {
                        changed |= AssignField(field, component, matchingTransform.gameObject);
                    }
                    else if (fieldType.IsSubclassOf(typeof(Component)))
                    {
                        // Try and find a matching component
                        var matchingComponent = matchingTransform.gameObject.GetComponent(fieldType);
                        if (matchingComponent)
                        {
                            // Found matching component - set
                            changed |= AssignField(field, component, matchingComponent);
                        }
                    }
                }
            }
            
            // Bind methods!
            var methods = componentType.GetMethods().Where(m=>m.GetCustomAttributes(typeof(BindFigmaButtonPress), false).Length > 0)
                .ToArray();

            foreach (var method in methods)
            {
                var buttonPressMethodAttribute = (BindFigmaButtonPress) method.GetCustomAttribute(typeof(BindFigmaButtonPress));
                //Debug.Log($"Attempting to bind method {method.Name} to button {buttonPressMethodAttribute.TargetButtonName}");
                var targetButtonTransform=GetChildTransformByName(gameObject.transform, buttonPressMethodAttribute.TargetButtonName, true,MAX_SEARCH_DEPTH_FOR_TRANSFORMS);
                if (targetButtonTransform != null)
                {
                    // Found matching transform, try and get button
                    var targetButton = targetButtonTransform.GetComponent<Button>();
                    if (targetButton != null)
                    {
                        //Debug.Log($"Found button on object {targetButtonTransform.name}");
                        // Some info here - https://stackoverflow.com/questions/40655089/how-to-add-persistent-listener-to-button-onclick-event-in-unity-editor-script
                        // And here https://stackoverflow.com/questions/47367429/is-it-possible-to-turn-a-string-of-a-function-name-to-a-unityaction
     
                       // Create a delegate for this method on this instance
                       UnityAction action = (UnityAction) Delegate.CreateDelegate(typeof(UnityAction),component, method, true);
                       // Assign this to the target button
                       UnityEventTools.AddPersistentListener(targetButton.onClick, action);
                       changed = true;
                    }
                }
            }

            return changed;
        }

        private static bool AssignField(FieldInfo field, Component component, UnityEngine.Object value)
        {
            if ((field.GetValue(component) as UnityEngine.Object) == value) return false;
            field.SetValue(component, value);
            return true;
        }

        /// <summary>
        /// Finds a child node (case insensitive)
        /// </summary>
        /// <param name="transform"></param>
        /// <param name="childName"></param>
        /// <param name="caseInsensitive"></param>
        /// <returns></returns>
        private static Transform GetChildTransformByName(Transform transform, string childName,bool caseInsensitive,int depthSearch)
        {
            var numChildren = transform.childCount;
            for (var i = 0; i < numChildren; i++)
            {
                var childTransform = transform.GetChild(i);
                if (CheckNodeNameMatches(childTransform, childName, caseInsensitive)) return childTransform;
            }

            if (depthSearch > 0)
            {
                for (var i = 0; i < numChildren; i++)
                {
                    var childTransform = transform.GetChild(i);
                    var foundInChildNode =
                        GetChildTransformByName(childTransform, childName, caseInsensitive, depthSearch - 1);
                    if (foundInChildNode != null) return foundInChildNode;
                }
            }
            return null;
        }

        private static bool CheckNodeNameMatches(Transform transform, string nameMatch, bool caseInsensitive)
        {
            if (caseInsensitive && transform.name == nameMatch) return true;
            if (!caseInsensitive && String.Equals(transform.name, nameMatch, StringComparison.CurrentCultureIgnoreCase)) return true;

            // If this contains an underscore, check the substring after
            // This is to allow matches of fields such as m_ScoreLabel as "ScoreLabel" from figma doc
            if (nameMatch.Contains("_"))
            {
                return CheckNodeNameMatches(transform, nameMatch.Substring(nameMatch.IndexOf("_", StringComparison.Ordinal) + 1),
                    caseInsensitive);
            }
            
            return false;
        }
        
        
        
        /// <summary>
        /// Finds a type of any kind by name (case insensitive) in every loaded assembly. With a namespace set, only a type
        /// in that namespace matches. Scans all assemblies on each call; binding uses <see cref="BuildBehaviourTypeLookup"/>
        /// </summary>
        /// <param name="nameSpace">Empty or null to accept any namespace</param>
        /// <param name="name"></param>
        public static Type GetTypeByName(string nameSpace,string name)
        {
            var candidateTypes = new List<Type>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type type in assembly.GetTypes())
                {
                    if (String.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase)) candidateTypes.Add(type);
                }
            }
            candidateTypes.Sort(CompareBindingPreference);
            return SelectType(candidateTypes, nameSpace);
        }

        /// <summary>
        /// Every MonoBehaviour that can be attached (not abstract, not open generic, not from a Unity assembly), keyed by
        /// type name (case insensitive). Each list is in <see cref="CompareBindingPreference"/> order
        /// </summary>
        private static Dictionary<string, List<Type>> BuildBehaviourTypeLookup()
        {
            var behaviourTypesByName = new Dictionary<string, List<Type>>(StringComparer.OrdinalIgnoreCase);
            foreach (var type in TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
            {
                if (type.IsAbstract || type.ContainsGenericParameters || IsUnityAssembly(type)) continue;
                if (!behaviourTypesByName.TryGetValue(type.Name, out var candidateTypes))
                {
                    candidateTypes = new List<Type>();
                    behaviourTypesByName.Add(type.Name, candidateTypes);
                }
                candidateTypes.Add(type);
            }
            foreach (var candidateTypes in behaviourTypesByName.Values)
            {
                if (candidateTypes.Count > 1) candidateTypes.Sort(CompareBindingPreference);
            }
            return behaviourTypesByName;
        }

        // A node named "Image" or "Button" must not gain a UGUI component; binding is for project behaviours
        private static bool IsUnityAssembly(Type type)
        {
            var assemblyName = type.Assembly.GetName().Name;
            return assemblyName.StartsWith("UnityEngine", StringComparison.Ordinal) ||
                   assemblyName.StartsWith("UnityEditor", StringComparison.Ordinal) ||
                   assemblyName.StartsWith("Unity.", StringComparison.Ordinal);
        }

        /// <summary>
        /// First type in the list whose namespace matches (case insensitive), or the first type when no namespace is set
        /// </summary>
        private static Type SelectType(List<Type> orderedCandidates, string nameSpace)
        {
            if (string.IsNullOrEmpty(nameSpace)) return orderedCandidates.Count > 0 ? orderedCandidates[0] : null;
            return orderedCandidates.Find(type => String.Equals(type.Namespace, nameSpace, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Stable order for types sharing a name: global namespace first, then by full name, then by assembly name (ordinal)
        /// </summary>
        private static int CompareBindingPreference(Type a, Type b)
        {
            var globalNamespaceFirst = string.IsNullOrEmpty(b.Namespace).CompareTo(string.IsNullOrEmpty(a.Namespace));
            if (globalNamespaceFirst != 0) return globalNamespaceFirst;
            var byFullName = string.CompareOrdinal(a.FullName, b.FullName);
            return byFullName != 0 ? byFullName : string.CompareOrdinal(a.Assembly.GetName().Name, b.Assembly.GetName().Name);
        }

        /// <summary>
        /// Bind behaviours to every component and flowScreen generated during the process
        /// </summary>
        /// <param name="figmaImportProcessData"></param>
        public static void BindBehaviours(FigmaImportProcessData figmaImportProcessData)
        {
            var behaviourTypesByName = BuildBehaviourTypeLookup();

            // Add all components and flowScreen prefabs, to apply behaviours
            var allComponentPrefabsToBindBehaviours = figmaImportProcessData.ComponentData.AllComponentPrefabs;
            allComponentPrefabsToBindBehaviours.AddRange(figmaImportProcessData.ScreenPrefabs);

            var changedPrefabCount = 0;
            foreach (var sourcePrefab in allComponentPrefabsToBindBehaviours)
            {
                string prefabAssetPath = AssetDatabase.GetAssetPath(sourcePrefab);
                GameObject instantiatedPrefab = PrefabUtility.LoadPrefabContents(prefabAssetPath);
                var prefabChanged = BindBehaviourToNodeAndChildren(instantiatedPrefab, figmaImportProcessData, behaviourTypesByName);

                // Write prefab with changes
                if (prefabChanged)
                {
                    PrefabUtility.SaveAsPrefabAsset(instantiatedPrefab, prefabAssetPath);
                    changedPrefabCount++;
                }
                PrefabUtility.UnloadPrefabContents(instantiatedPrefab);
            }

            FigmaImportTimer.SetDetail("Bind behaviours",
                $"{allComponentPrefabsToBindBehaviours.Count} prefab(s), {changedPrefabCount} changed and saved");
        }

        /// <summary>
        /// Bind behaviour to all nodes within a tree structure 
        /// </summary>
        /// <param name="targetGameObject"></param>
        /// <param name="figmaImportProcessData"></param>
        /// <param name="behaviourTypesByName"></param>
        /// <returns>True when any node in the tree was changed</returns>
        private static bool BindBehaviourToNodeAndChildren(GameObject targetGameObject,FigmaImportProcessData figmaImportProcessData,
            Dictionary<string, List<Type>> behaviourTypesByName)
        {
           var changed = false;
           // Apply depth-first application of node behaviours (as assumes parent nodes will want ref to children rather than vice versa)
           var numChildren = targetGameObject.transform.childCount;
           for (var i = 0; i < numChildren; i++)
           {
               // Apply to child nodes first
               var childTransform = targetGameObject.transform.GetChild(i);
               changed |= BindBehaviourToNodeAndChildren(childTransform.gameObject, figmaImportProcessData, behaviourTypesByName);
           }
           // Finally apply to this node
           changed |= BindBehaviourToNode(targetGameObject, figmaImportProcessData, behaviourTypesByName);
           return changed;
        }
    }
}