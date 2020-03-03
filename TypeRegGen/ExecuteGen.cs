#define USE_VOID_PTR // if defined, the Execute_Gen method will have `void*` or `int` parameter for everything.
                     // This works around a Burst issue, but makes the decompiled code more difficult to read.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Unity.Entities.BuildUtils;

namespace Unity.ZeroPlayer
{
    class ExecuteGen
    {
        string GetComponentTypesFnName = "GetComponentTypes_Gen";
        string GetJobReflectionFnName = "GetJobReflection_Gen";

        List<AssemblyDefinition>
            m_assemblies;

        AssemblyDefinition
            m_entityAssembly,
            m_zeroJobsAssembly,
            m_burstAssembly;

        TypeDefinition m_jobForEachExtensions;

        enum Access
        {
            ReadWrite,
            ReadOnly,
            Exclude
        }

        struct Component
        {
            public TypeReference type;
            public Access access;
        }

        public ExecuteGen(List<AssemblyDefinition> assemblies)
        {
            m_assemblies = assemblies;
            m_entityAssembly = assemblies.First(asm => asm.Name.Name == "Unity.Entities");
            m_zeroJobsAssembly = assemblies.First(asm => asm.Name.Name == "Unity.ZeroJobs");
            m_burstAssembly = assemblies.First(asm => asm.Name.Name == "Unity.Burst");
            m_jobForEachExtensions = m_entityAssembly.MainModule.Types.First(t => t.FullName == "Unity.Entities.JobForEachExtensions");
        }

        static bool ParamIsReadOnly(ParameterDefinition param)
        {
            if (param.HasCustomAttributes &&
                param.CustomAttributes.FirstOrDefault(p =>
                    p.AttributeType.FullName == "Unity.Collections.ReadOnlyAttribute") != null)
            {
                return true;
            }

            return false;
        }

        void AddAbstractMethodsToIBaseJobForEach(out MethodDefinition getComponentTypesMethod, out MethodDefinition getJobReflectionMethod)
        {
            TypeDefinition iBaseJobForEach = m_jobForEachExtensions.NestedTypes.First(t => t.FullName == "Unity.Entities.JobForEachExtensions/IBaseJobForEach");
            ModuleDefinition module = iBaseJobForEach.Module;
            var compTypeTD = m_entityAssembly.MainModule.Types.First(t => t.FullName == "Unity.Entities.ComponentType");
            var compTyeArrayTD = compTypeTD.MakeArrayType();

            getComponentTypesMethod = new MethodDefinition(GetComponentTypesFnName,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.Abstract | MethodAttributes.NewSlot,
                module.ImportReference(compTyeArrayTD));
            getComponentTypesMethod.Parameters.Add(new ParameterDefinition("processCount", ParameterAttributes.Out, module.ImportReference(typeof(int).MakeByRefType())));
            getComponentTypesMethod.Parameters.Add(new ParameterDefinition("changedFilter", ParameterAttributes.Out, module.ImportReference(compTyeArrayTD).MakeByReferenceType()));
            iBaseJobForEach.Methods.Add(getComponentTypesMethod);

            getJobReflectionMethod = new MethodDefinition(GetJobReflectionFnName,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.Abstract | MethodAttributes.NewSlot,
                module.ImportReference(typeof(IntPtr)));
            getJobReflectionMethod.Parameters.Add(new ParameterDefinition("isParallelFor", ParameterAttributes.None, module.ImportReference(typeof(bool))));
            iBaseJobForEach.Methods.Add(getJobReflectionMethod);
        }

        public void PatchForEachInterface()
        {
            var module = m_jobForEachExtensions.Module;
            AddAbstractMethodsToIBaseJobForEach(out MethodDefinition getComponentTypesMethod, out MethodDefinition getJobReflectionMethod);

            // And change the existing helpers to call them.
            {
                MethodDefinition getComponentTypes = m_jobForEachExtensions.Methods.First(t => t.Name == "GetComponentTypes");
                getComponentTypes.Body.Instructions.Clear();
                var il = getComponentTypes.Body.GetILProcessor();

                // return self.GetComponentTypes_Gen(out processCount, out changedFilter);
                il.Emit(OpCodes.Ldarga, getComponentTypes.Parameters[0]);
                il.Emit(OpCodes.Ldarg, getComponentTypes.Parameters[1]);
                il.Emit(OpCodes.Ldarg, getComponentTypes.Parameters[2]);

                il.Emit(OpCodes.Constrained, getComponentTypes.GenericParameters[0]);
                il.Emit(OpCodes.Callvirt, module.ImportReference(getComponentTypesMethod));
                il.Emit(OpCodes.Ret);
            }

            {
                MethodDefinition getComponentTypes = m_jobForEachExtensions.Methods.First(t => t.Name == "GetJobReflection");
                getComponentTypes.Body.Instructions.Clear();
                var il = getComponentTypes.Body.GetILProcessor();

                // return self.GetComponentTypes_Gen(out processCount, out changedFilter);
                il.Emit(OpCodes.Ldarga, getComponentTypes.Parameters[0]);
                il.Emit(OpCodes.Ldarg, getComponentTypes.Parameters[1]);

                il.Emit(OpCodes.Constrained, getComponentTypes.GenericParameters[0]);
                il.Emit(OpCodes.Callvirt, module.ImportReference(getJobReflectionMethod));
                il.Emit(OpCodes.Ret);
            }
        }

        bool IsChangeFilter(TypeDefinition td)
        {
            return td.HasCustomAttributes && (td.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "ChangedFilterAttribute") != null);
        }

        void GenerateGetComponentTypes(AssemblyDefinition asm,
            TypeDefinition jobStruct,
            List<ParameterDefinition> executeParams)
        {
            var module = asm.MainModule;

            var componentTypeDef = m_entityAssembly.MainModule.Types.First(t => t.FullName == "Unity.Entities.ComponentType");
            var componentArrayTypeDef = componentTypeDef.MakeArrayType();
            var openReadWriteRef = asm.MainModule.ImportReference(
                componentTypeDef.Methods.First(i => i.Name == "ReadWrite" && i.GenericParameters.Count == 1));

            var openReadOnlyRef = asm.MainModule.ImportReference(
                componentTypeDef.Methods.First(i => i.Name == "ReadOnly" && i.GenericParameters.Count == 1));

            var openExcludeRef = asm.MainModule.ImportReference(
                componentTypeDef.Methods.First(i => i.Name == "Exclude" && i.GenericParameters.Count == 1));


            // Method
            var method = new MethodDefinition(GetComponentTypesFnName,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                module.ImportReference(componentArrayTypeDef));
            var processCountArg = new ParameterDefinition("processCount", ParameterAttributes.Out, module.ImportReference(typeof(int).MakeByRefType()));
            method.Parameters.Add(processCountArg);
            var changedFilterArg = new ParameterDefinition("changedFilter", ParameterAttributes.Out, module.ImportReference(componentArrayTypeDef.MakeByReferenceType()));
            method.Parameters.Add(changedFilterArg);

            // Locals
            var arrayVar = new VariableDefinition(module.ImportReference(componentArrayTypeDef));
            method.Body.Variables.Add(arrayVar);
            method.Body.InitLocals = true;

            // Body
            List<Component> components = FindComponents(jobStruct, executeParams);

            int nChangedFilters = 0;
            for (int i = 0; i < components.Count; ++i)
            {
                if (IsChangeFilter(components[i].type.Resolve()))
                    nChangedFilters++;
            }

            var il = method.Body.GetILProcessor();

            // ComponentType[] array = new ComponentType[3];
            il.Emit(OpCodes.Ldc_I4, components.Count);
            il.Emit(OpCodes.Newarr, module.ImportReference(componentTypeDef));
            il.Emit(OpCodes.Stloc, arrayVar);

            // changedFilter = new ComponentType[2];
            il.Emit(OpCodes.Ldarg, changedFilterArg);
            il.Emit(OpCodes.Ldc_I4, nChangedFilters);
            il.Emit(OpCodes.Newarr, module.ImportReference(componentTypeDef));
            il.Emit(OpCodes.Stind_Ref);

            int changeCount = 0;
            for (int i = 0; i < components.Count; ++i)
            {
                // array[0] = ComponentType.ReadWrite<Something>();
                il.Emit(OpCodes.Ldloc, arrayVar);
                il.Emit(OpCodes.Ldc_I4, i);

                MethodReference componentCreatorMethodRef = null;
                if (components[i].access == Access.ReadOnly) componentCreatorMethodRef = openReadOnlyRef;
                else if (components[i].access == Access.ReadWrite) componentCreatorMethodRef = openReadWriteRef;
                else if (components[i].access == Access.Exclude) componentCreatorMethodRef = openExcludeRef;

                MethodReference closedComponentCreatorMethodRef =
                    TypeRegGen.MakeGenericMethodSpecialization(componentCreatorMethodRef,
                        asm.MainModule.ImportReference(components[i].type));

                il.Emit(OpCodes.Call, closedComponentCreatorMethodRef);
                il.Emit(OpCodes.Stelem_Any, module.ImportReference(componentTypeDef));

                if (IsChangeFilter(components[i].type.Resolve()))
                {
                    il.Emit(OpCodes.Ldarg, changedFilterArg);
                    il.Emit(OpCodes.Ldc_I4, changeCount++);
                    il.Emit(OpCodes.Call, closedComponentCreatorMethodRef);
                    il.Emit(OpCodes.Stelem_Any, module.ImportReference(componentTypeDef));
                }
            }

            // processCount = 3;
            il.Emit(OpCodes.Ldarg, processCountArg);
            il.Emit(OpCodes.Ldc_I4, components.Count);
            il.Emit(OpCodes.Stind_I4);

            // return array;
            il.Emit(OpCodes.Ldloc, arrayVar);
            il.Emit(OpCodes.Ret);

            jobStruct.Methods.Add(method);
        }

        void GenerateJobReflection(AssemblyDefinition asm,
            TypeDefinition jobStruct,
            bool usesEntity,
            List<ParameterDefinition> executeParams)
        {
            var module = asm.MainModule;

            var method = new MethodDefinition(GetJobReflectionFnName,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                module.ImportReference(typeof(IntPtr)));
            var isParallelArg = new ParameterDefinition("isParallelFor", ParameterAttributes.None, module.ImportReference(typeof(bool)));
            method.Parameters.Add(isParallelArg);
            jobStruct.Methods.Add(method);

            TypeDefinition jobTypeDef = m_zeroJobsAssembly.MainModule.Types.First(i =>
                i.FullName == "Unity.Jobs.LowLevel.Unsafe.JobType");
            var jobTypeVar = new VariableDefinition(module.ImportReference(jobTypeDef));
            method.Body.Variables.Add(jobTypeVar);
            method.Body.InitLocals = true;

            var il = method.Body.GetILProcessor();
            Instruction target = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg, isParallelArg);
            il.Emit(OpCodes.Brfalse, target);

            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stloc, jobTypeVar);
            il.Append(target);

            StringBuilder builder = new StringBuilder("JobStruct_Process_");
            List<TypeReference> genericArgs = new List<TypeReference>();
            genericArgs.Add(jobStruct);

            if (usesEntity)
            {
                builder.Append("E");
            }

            for (int i = 0; i < executeParams.Count; ++i)
            {
                var paramType = executeParams[i].ParameterType;
                if (paramType.FullName.StartsWith("Unity.Entities.DynamicBuffer"))
                {
                    var gi = paramType as GenericInstanceType;
                    paramType = gi.GenericArguments[0];
                    if (!paramType.IsComponentType())
                        throw new ArgumentException($"Execute function for job struct {jobStruct.FullName} contains a generic param that doesn't contain a component as the first generic argument. Found '{paramType.FullName}'");

                    builder.Append("B");
                    genericArgs.Add(paramType);
                }
                else
                {
                    builder.Append("C");
                    genericArgs.Add(paramType.GetElementType());
                }
            } 

            builder.Append("`");

            // Unity.Entities.JobForEachExtensions.JobStruct_Process_C
            TypeReference openStruct = m_jobForEachExtensions.NestedTypes.First(t => t.FullName.StartsWith("Unity.Entities.JobForEachExtensions/" + builder));
            MethodDefinition openInit = openStruct.Resolve().Methods.First(m => m.Name == "Initialize");
            MethodReference closedInit = openInit.MakeHostInstanceGeneric(genericArgs.ToArray());

            il.Emit(OpCodes.Ldloc, jobTypeVar);
            il.Emit(OpCodes.Call, module.ImportReference(closedInit));
            il.Emit(OpCodes.Ret);
        }

        // TODO GenerateForEachMethods should use this code.
        // It's extracted from GenerateForEachMethods. This method (correctly) extracts TypeReferences instead
        // of ParameterDefinitions. If GenerateForEachMethods switches to using ForEachExecuteTypes, a lot
        // of downstream code needs to be also cleaned up and refactored. Not a big dealt, but its own PR.
        static public List<TypeReference> ForEachExecuteTypes(MethodDefinition executeMethod, out bool usesEntity)
        {
            usesEntity = false;
            if (executeMethod.Parameters[0].ParameterType.FullName == "Unity.Entities.Entity")
            {
                usesEntity = true;
                if (executeMethod.Parameters[1].ParameterType.MetadataType != MetadataType.Int32)
                {
                    throw new InvalidOperationException(
                        "FindScheduleMethod: Execute method specifies an Entity, but not an int index");
                }
            }
            // Incredibly confusing to extract the parameters later, so extract them here and re-use.
            List<TypeReference> executeTypes = new List<TypeReference>();
            for (int i = usesEntity ? 2 : 0; i < executeMethod.Parameters.Count; ++i)
            {
                executeTypes.Add(executeMethod.Parameters[i].ParameterType);
            }

            return executeTypes;
        }

        public void GenerateForEachMethods()
        {
            foreach (var asm in m_assemblies)
            {
                foreach (var type in asm.MainModule.GetAllTypes())
                {
                    if (type.IsStructWithInterface("Unity.Entities.JobForEachExtensions/IBaseJobForEach"))
                    {
                        // The Job is required to have an Execute method - with the correct
                        // parameters. It's a handy place to grab them, so find that method and use it.
                        MethodDefinition executeMethod = type.Methods.First(f => f.Name == "Execute");

                        // There are (so far) 2 flavors of Schedule.
                        //     Schedule<T>(this T jobData, SystemBase system, JobHandle dependsOn = default(JobHandle))
                        //     Schedule<T>(this T jobData, EntityQuery query, JobHandle dependsOn = default(JobHandle))
                        // The only difference being the 2nd parameter.

                        // Extract the ComponentData used by the Execute
                        bool usesEntity = false;
                        if (executeMethod.Parameters[0].ParameterType.FullName == "Unity.Entities.Entity")
                        {
                            usesEntity = true;
                            if (executeMethod.Parameters[1].ParameterType.MetadataType != MetadataType.Int32)
                            {
                                throw new InvalidOperationException(
                                    $"Execute method in {type.FullName} specifies an Entity, but not an int index.");
                            }
                        }
                        // Incredibly confusing to extract the parameters later, so extract them here and re-use.
                        List<ParameterDefinition> executeParams = new List<ParameterDefinition>();
                        for (int i = usesEntity ? 2 : 0; i < executeMethod.Parameters.Count; ++i)
                        {
                            executeParams.Add(executeMethod.Parameters[i]);
                        }

                        GenerateGetComponentTypes(asm, type, executeParams);
                        GenerateJobReflection(asm, type, usesEntity, executeParams);
                    }
                }
            }
        }

        List<Component> FindComponents(TypeDefinition jobStruct, List<ParameterDefinition> executeParams)
        {
            List<Component> comps = new List<Component>();
            List<TypeDefinition> require = new List<TypeDefinition>();
            List<TypeDefinition> exclude = new List<TypeDefinition>();

            if (jobStruct.IsStructWithInterface("Unity.Entities.JobForEachExtensions/IBaseJobForEach"))
            {
                foreach (var attr in jobStruct.CustomAttributes)
                {
                    bool hasExclude = attr.AttributeType.FullName == "Unity.Entities.ExcludeComponentAttribute";
                    bool hasRequire = attr.AttributeType.FullName == "Unity.Entities.RequireComponentTagAttribute";

                    if (!hasExclude && !hasRequire) continue;

                    if (attr.HasConstructorArguments)
                    {
                        foreach (var arg in attr.ConstructorArguments)
                        {
                            var cArr = arg.Value as CustomAttributeArgument[];
                            if (cArr != null)
                            {
                                CustomAttributeArgument[] caa = cArr;
                                for (int i = 0; i < caa.Length; ++i)
                                {
                                    // In the same assembly, we get by a TypeDef,
                                    // in a different assembly, a TypeRef.
                                    var val = caa[i].Value;
                                    if (val is TypeReference)
                                    {
                                        TypeDefinition td = ((TypeReference)val).Resolve();
                                        if (hasExclude) exclude.Add(td);
                                        if (hasRequire) require.Add(td);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < executeParams.Count; ++i)
            {
                TypeReference componentRef = null;
                var paramType = executeParams[i].ParameterType;
                if (paramType.IsGenericInstance && paramType.FullName.StartsWith("Unity.Entities.DynamicBuffer"))
                {
                    var gi = paramType as GenericInstanceType;
                    var componentType = gi.GenericArguments[0];
                    if (!componentType.IsComponentType())
                        throw new ArgumentException($"Execute function for job struct {jobStruct.FullName} contains a generic param that doesn't contain a component as the first generic argument. Found '{componentType.FullName}'");
                    componentRef = componentType;
                }
                else
                {
                    componentRef = paramType.GetElementType(); 
                }
                
                comps.Add(new Component()
                {
                    type = componentRef,
                    access = ParamIsReadOnly(executeParams[i]) ? Access.ReadOnly : Access.ReadWrite
                });
            }

            for (int i = 0; i < require.Count; ++i)
            {
                comps.Add(new Component()
                {
                    type = require[i],
                    access = Access.ReadOnly
                });
            }

            for (int i = 0; i < exclude.Count; ++i)
            {
                comps.Add(new Component()
                {
                    type = exclude[i],
                    access = Access.Exclude
                });
            }

            return comps;
        }

        // Searches for calls to IJobForEach.Schedule, Run, or ScheduleSingle
        // And patches to call the wrapper function.
        // Example:
        //     Big-ECS:
        //         myJob.Run();  // calls Unity.Entities.JobForEachExtensions.Run, which
        //                       // does a BIG if-case type check to call:
        //                       // ScheduleInternal_C(ref jobData, system, null, -1, dependsOn, ScheduleMode.Run);
        //     DOTS-Runtime:
        //        myJob.Run();
        // patched to -> myJob.Run_To_System_C();
        // which calls:  ScheduleInternal_C(ref jobData, system, null, -1, dependsOn, ScheduleMode.Run);
        //
        // BOTH cases call ScheduleInternal_C(ref jobData, system, null, -1, dependsOn, ScheduleMode.Run),
        // which is important. But DOTS-Runtime avoids the if case, which hopefully cuts down code size
        // a bit since the linker can strip the methods down to a know set in use.
        //
        public void PatchScheduleCalls(AssemblyDefinition asm, IEnumerable<Instruction> outerList)
        {
            string[] callNames =
            {
                "Unity.Entities.JobForEachExtensions::Schedule",
                "Unity.Entities.JobForEachExtensions::Run",
                "Unity.Entities.JobForEachExtensions::ScheduleSingle"
            };

            string[] searchNames =
            {
                "Unity.Entities.JobForEachExtensions::Schedule<",
                "Unity.Entities.JobForEachExtensions::Run<",
                "Unity.Entities.JobForEachExtensions::ScheduleSingle<"
            };

            for (int i = 0; i < callNames.Length; ++i)
            {
                var giList = outerList.Where(inst =>
                    (inst.Operand is MethodReference)
                    && (inst.Operand as MethodReference).ContainsGenericParameter
                    && ((inst.Operand as MethodReference).FullName.Contains(searchNames[i])));

                foreach (var g in giList)
                {
                    var gMethod = g.Operand as GenericInstanceMethod;
                    if (gMethod == null)
                        throw new InvalidOperationException($"{gMethod.FullName} was expected to be a generic method!");

                    var jobStruct = gMethod.GenericArguments[0].Resolve();

                    // There are (so far) 2 flavors of Schedule.
                    //     Schedule<T>(this T jobData, ComponentSystemBase system, JobHandle dependsOn = default(JobHandle))
                    //     Schedule<T>(this T jobData, EntityQuery query, JobHandle dependsOn = default(JobHandle))
                    // The only difference being the 2nd parameter.

                    bool isQuery = gMethod.Parameters.Count >= 2 &&
                        gMethod.Parameters[1].ParameterType.FullName == "Unity.Entities.EntityQuery";

                    // Need the postfix - ECC, EBCC, C, etc. This is pulled from the
                    // IJobForEach interface - of which there are several.
                    string postFix = null;
                    foreach (var iface in jobStruct.Interfaces)
                    {
                        const string ForEachWithEntity = "Unity.Entities.IJobForEachWithEntity_";
                        const string ForEach = "Unity.Entities.IJobForEach_";

                        if (iface.InterfaceType.FullName.StartsWith(ForEach))
                        {
                            var sub = iface.InterfaceType.FullName.Substring(ForEach.Length);
                            int index = sub.IndexOf('`');
                            postFix = sub.Substring(0, index);
                            break;
                        }
                        if (iface.InterfaceType.FullName.StartsWith(ForEachWithEntity))
                        {
                            var sub = iface.InterfaceType.FullName.Substring(ForEachWithEntity.Length);
                            int index = sub.IndexOf('`');
                            postFix = sub.Substring(0, index);
                            break;
                        }
                    }
                    if (postFix == null)
                        throw new InvalidOperationException($"Internal error finding IJobForEach prefix while processing {jobStruct.FullName}.");

                    var typeName = isQuery ? "_To_Query_" : "_To_System_";
                    var fName = "Unity.Jobs.JobHandle " + callNames[i] + typeName + postFix + "(";
                    MethodDefinition md = m_jobForEachExtensions.Methods.First(m => m.FullName.StartsWith(fName));

                    md.IsPublic = true;
                    g.Operand = asm.MainModule.ImportReference(TypeRegGen.MakeGenericMethodSpecialization(md, gMethod.GenericArguments[0]));
                }
            }

        }
    }
}

