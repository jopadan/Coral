﻿using Coral.Managed.Interop;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Coral.Managed;

public enum AssemblyLoadStatus
{
	Success, FileNotFound, FileLoadFailure, InvalidFilePath, InvalidAssembly, UnknownError
}

public static class AssemblyLoader
{
	private static readonly Dictionary<Type, AssemblyLoadStatus> s_AssemblyLoadErrorLookup = new();
	private static readonly Dictionary<int, Assembly> s_AssemblyCache = new();
	private static AssemblyLoadStatus s_LastLoadStatus = AssemblyLoadStatus.Success;

	private static readonly AssemblyLoadContext? s_CoralAssemblyLoadContext;
	private static AssemblyLoadContext? s_AppAssemblyLoadContext;

	static AssemblyLoader()
	{
		s_AssemblyLoadErrorLookup.Add(typeof(BadImageFormatException), AssemblyLoadStatus.InvalidAssembly);
		s_AssemblyLoadErrorLookup.Add(typeof(FileNotFoundException), AssemblyLoadStatus.FileNotFound);
		s_AssemblyLoadErrorLookup.Add(typeof(FileLoadException), AssemblyLoadStatus.FileLoadFailure);
		s_AssemblyLoadErrorLookup.Add(typeof(ArgumentNullException), AssemblyLoadStatus.InvalidFilePath);
		s_AssemblyLoadErrorLookup.Add(typeof(ArgumentException), AssemblyLoadStatus.InvalidFilePath);

		s_CoralAssemblyLoadContext = AssemblyLoadContext.GetLoadContext(typeof(AssemblyLoader).Assembly);
		s_CoralAssemblyLoadContext!.Resolving += ResolveAssembly;
			
		CacheCoralAssemblies();
	}

	private static void CacheCoralAssemblies()
	{
		foreach (var assembly in s_CoralAssemblyLoadContext!.Assemblies)
		{
			int assemblyId = assembly.GetName().FullName.GetHashCode();
			s_AssemblyCache.Add(assemblyId, assembly);
		}
	}

	internal static Assembly? ResolveAssembly(AssemblyLoadContext? InAssemblyLoadContext, AssemblyName InAssemblyName)
	{
		try
		{
			int assemblyId = InAssemblyName.FullName.GetHashCode();
			
			if (s_AssemblyCache.TryGetValue(assemblyId, out var cachedAssembly))
			{
				return cachedAssembly;
			}

			foreach (var loadContext in AssemblyLoadContext.All)
			{
				foreach (var assembly in loadContext.Assemblies)
				{
					if (assembly.GetName().Name != InAssemblyName.Name)
						continue;
					
					s_AssemblyCache.Add(assemblyId, assembly);
					return assembly;
				}
			}
		}
		catch (Exception ex)
		{
			ManagedHost.HandleException(ex);
		}

		return null;
	}

	[UnmanagedCallersOnly]
	private static int LoadAssembly(UnmanagedString InAssemblyFilePath)
	{
		try
		{
			if (InAssemblyFilePath.IsNull())
			{
				s_LastLoadStatus = AssemblyLoadStatus.InvalidFilePath;
				return -1;
			}

			if (!File.Exists(InAssemblyFilePath))
			{
				Console.WriteLine($"File {InAssemblyFilePath} not found!");
				s_LastLoadStatus = AssemblyLoadStatus.FileNotFound;
				return -1;
			}

			if (s_AppAssemblyLoadContext == null)
			{
				s_AppAssemblyLoadContext = new AssemblyLoadContext("AppAssemblyContext", true);
				s_AppAssemblyLoadContext.Resolving += ResolveAssembly;
				s_AppAssemblyLoadContext.Unloading += _ => { s_AppAssemblyLoadContext = null; };
			}

			Assembly? assembly = null;

			using (var file = MemoryMappedFile.CreateFromFile(InAssemblyFilePath))
			{
				using (var stream = file.CreateViewStream())
				{
					assembly = s_AppAssemblyLoadContext.LoadFromStream(stream);
				}
			}

			var assemblyName = assembly.GetName();
			int assemblyId = assemblyName.FullName.GetHashCode();
			s_AssemblyCache.Add(assemblyId, assembly);
			s_LastLoadStatus = AssemblyLoadStatus.Success;
			Console.WriteLine($"Loaded assembly {InAssemblyFilePath}");
			return assemblyId;
		}
		catch (Exception ex)
		{
			s_AssemblyLoadErrorLookup.TryGetValue(ex.GetType(), out s_LastLoadStatus);
			ManagedHost.HandleException(ex);
			return -1;
		}
	}

	[UnmanagedCallersOnly]
	private static unsafe void QueryAssemblyTypes(int InAssemblyId, ManagedHost.ReflectionType* OutTypes, int* OutTypeCount)
	{
		try
		{
			if (!s_AssemblyCache.TryGetValue(InAssemblyId, out var assembly))
			{
				return;
			}

			ReadOnlySpan<Type> assemblyTypes = assembly.GetTypes();

			if (OutTypeCount != null)
				*OutTypeCount = assemblyTypes.Length;

			if (OutTypes == null)
				return;

			for (int i = 0; i < assemblyTypes.Length; i++)
			{
				var type = assemblyTypes[i];

				var reflectionType = ManagedHost.BuildReflectionType(type);

				if (reflectionType == null)
					continue;

				OutTypes[i] = reflectionType.Value;
			}
		}
		catch (Exception ex)
		{
			ManagedHost.HandleException(ex);
		}
	}
		
	[UnmanagedCallersOnly]
	private static void UnloadAssemblyLoadContext(int InAssemblyId)
	{
		try
		{
			if (!s_AssemblyCache.TryGetValue(InAssemblyId, out var assembly))
			{
				Console.WriteLine("Tried unloading an assembly that wasn't previously loaded.");
				return;
			}

			var loadContext = AssemblyLoadContext.GetLoadContext(assembly);
			if (!loadContext!.IsCollectible)
			{
				Console.WriteLine("Tried unloading an assembly load context that isn't collectible!");
				return;
			}

			foreach (var loadedAssembly in loadContext.Assemblies)
			{
				int assemblyId = loadedAssembly.GetName().FullName.GetHashCode();
				s_AssemblyCache.Remove(assemblyId);
			}

			s_AssemblyCache.Remove(InAssemblyId);
			loadContext.Unload();
		}
		catch (Exception ex)
		{
			ManagedHost.HandleException(ex);
		}
	}

	[UnmanagedCallersOnly]
	private static AssemblyLoadStatus GetLastLoadStatus() => s_LastLoadStatus;

	[UnmanagedCallersOnly]
	private static UnmanagedString GetAssemblyName(int InAssemblyId)
	{
		if (!s_AssemblyCache.TryGetValue(InAssemblyId, out var assembly))
			return UnmanagedString.Null();

		var assemblyName = assembly.GetName();
		return UnmanagedString.FromString(assemblyName.Name);
	}

}
