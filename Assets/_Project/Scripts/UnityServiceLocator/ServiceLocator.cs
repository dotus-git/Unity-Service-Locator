﻿using System;
using System.Collections.Generic;
using System.Linq;
using Extensions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityServiceLocator
{
    public class ServiceLocator : MonoBehaviour
    {
        private const int k_defaultRootListCapacity = 16;
        private const string k_globalServiceLocatorName = "ServiceLocator [Global]";
        private const string k_sceneServiceLocatorName = "ServiceLocator [Scene]";
        private static ServiceLocator global;
        private static Dictionary<Scene, ServiceLocator> sceneContainers;
        private static List<GameObject> rootSceneGameObjects;

        private readonly ServiceManager services = new();

        /// <summary>
        ///     Gets the global ServiceLocator instance. Creates new if none exists.
        /// </summary>
        public static ServiceLocator Global
        {
            get
            {
                if (global) return global;

                var found = FindFirstObjectByType<ServiceLocatorGlobal>();
                if (found)
                {
                    found.BootstrapOnDemand();
                    return global;
                }

                var container = new GameObject(k_globalServiceLocatorName, typeof(ServiceLocator));
                container.AddComponent<ServiceLocatorGlobal>().BootstrapOnDemand();

                return global;
            }
        }

        private void OnDestroy()
        {
            if (this == global)
                global = null;
            else if (sceneContainers.ContainsValue(this))
                sceneContainers.Remove(gameObject.scene);
        }

        internal void ConfigureAsGlobal(bool dontDestroyOnLoad)
        {
            if (global == this)
            {
                Debug.LogWarning("ServiceLocator.ConfigureAsGlobal: Already configured as global", this);
            }
            else if (global != null)
            {
                Debug.LogError("ServiceLocator.ConfigureAsGlobal: Another ServiceLocator is already configured as global", this);
            }
            else
            {
                global = this;
                if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);
            }
        }

        internal void ConfigureForScene()
        {
            var scene = gameObject.scene;

            if (sceneContainers.ContainsKey(scene))
            {
                Debug.LogError("ServiceLocator.ConfigureForScene: Another ServiceLocator is already configured for this scene", this);
                return;
            }

            sceneContainers.Add(scene, this);
        }

        /// <summary>
        ///     Returns the <see cref="ServiceLocator" /> configured for the scene of a MonoBehaviour. Falls back to the global
        ///     instance.
        /// </summary>
        public static ServiceLocator ForSceneOf(MonoBehaviour mb)
        {
            var scene = mb.gameObject.scene;

            if (sceneContainers.TryGetValue(scene, out var container) && container != mb)
                return container;

            rootSceneGameObjects.Clear();
            scene.GetRootGameObjects(rootSceneGameObjects);

            var bootstrapper = rootSceneGameObjects
                .Select(go => go.GetComponent<ServiceLocatorScene>())
                .FirstOrDefault(o => o.Container != mb);

            if (bootstrapper)
            {
                bootstrapper.BootstrapOnDemand();
                return bootstrapper.Container;
            }

            return Global;
        }

        /// <summary>
        ///     Gets the closest ServiceLocator instance to the provided
        ///     MonoBehaviour in hierarchy, the ServiceLocator for its scene, or the global ServiceLocator.
        /// </summary>
        public static ServiceLocator For(MonoBehaviour mb)
            => mb.GetComponentInParent<ServiceLocator>().OrNull() ?? ForSceneOf(mb) ?? Global;

        /// <summary>
        ///     Registers a service to the ServiceLocator using the service's type.
        /// </summary>
        /// <param name="service">The service to register.</param>
        /// <typeparam name="T">Class type of the service to be registered.</typeparam>
        /// <returns>The ServiceLocator instance after registering the service.</returns>
        public ServiceLocator Register<T>(T service)
        {
            services.Register(service);
            return this;
        }

        /// <summary>
        ///     Registers a service to the ServiceLocator using a specific type.
        /// </summary>
        /// <param name="type">The type to use for registration.</param>
        /// <param name="service">The service to register.</param>
        /// <returns>The ServiceLocator instance after registering the service.</returns>
        public ServiceLocator Register(Type type, object service)
        {
            services.Register(type, service);
            return this;
        }

        /// <summary>
        ///     Gets a service of a specific type. If no service of the required type is found, an error is thrown.
        /// </summary>
        /// <param name="service">Service of type T to get.</param>
        /// <typeparam name="T">Class type of the service to be retrieved.</typeparam>
        /// <returns>The ServiceLocator instance after attempting to retrieve the service.</returns>
        public ServiceLocator Get<T>(out T service) where T : class
        {
            if (TryGetService(out service)) return this;

            if (TryGetNextInHierarchy(out var container))
            {
                container.Get(out service);
                return this;
            }

            throw new ArgumentException($"ServiceLocator.Get: Service of type {typeof(T).FullName} not registered");
        }

        /// <summary>
        ///     Allows retrieval of a service of a specific type. An error is thrown if the required service does not exist.
        /// </summary>
        /// <typeparam name="T">Class type of the service to be retrieved.</typeparam>
        /// <returns>Instance of the service of type T.</returns>
        public T Get<T>() where T : class
        {
            var type = typeof(T);
            T service = null;

            if (TryGetService(type, out service)) return service;

            if (TryGetNextInHierarchy(out var container))
                return container.Get<T>();

            throw new ArgumentException($"Could not resolve type '{typeof(T).FullName}'.");
        }

        /// <summary>
        ///     Tries to get a service of a specific type. Returns whether or not the process is successful.
        /// </summary>
        /// <param name="service">Service of type T to get.</param>
        /// <typeparam name="T">Class type of the service to be retrieved.</typeparam>
        /// <returns>True if the service retrieval was successful, false otherwise.</returns>
        public bool TryGet<T>(out T service) where T : class
        {
            var type = typeof(T);
            service = null;

            if (TryGetService(type, out service))
                return true;

            return TryGetNextInHierarchy(out var container) && container.TryGet(out service);
        }

        private bool TryGetService<T>(out T service) where T : class
            => services.TryGet(out service);

        private bool TryGetService<T>(Type type, out T service) where T : class
            => services.TryGet(out service);

        private bool TryGetNextInHierarchy(out ServiceLocator container)
        {
            if (this == global)
            {
                container = null;
                return false;
            }

            container = transform.parent.OrNull()?.GetComponentInParent<ServiceLocator>().OrNull() ?? ForSceneOf(this);
            return container != null;
        }

        // https://docs.unity3d.com/ScriptReference/RuntimeInitializeOnLoadMethodAttribute.html
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            global = null;
            sceneContainers = new Dictionary<Scene, ServiceLocator>();
            rootSceneGameObjects = new List<GameObject>(k_defaultRootListCapacity); // https://docs.unity3d.com/ScriptReference/SceneManagement.Scene.GetRootGameObjects.html "Please make sure the list capacity is bigger than Scene.rootCount, then Unity will not allocate memory internally."
        }

#if UNITY_EDITOR
        [MenuItem("GameObject/ServiceLocator/Add Global")]
        private static void AddGlobal()
        {
            var go = new GameObject(k_globalServiceLocatorName, typeof(ServiceLocatorGlobal));
        }

        [MenuItem("GameObject/ServiceLocator/Add Scene")]
        private static void AddScene()
        {
            var go = new GameObject(k_sceneServiceLocatorName, typeof(ServiceLocatorScene));
        }
#endif
    }
}