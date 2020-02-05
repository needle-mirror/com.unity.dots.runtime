using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Unity.Entities.BuildUtils;
using Unity.IL2CPP.ILPreProcessor;
using FieldTypeTuple = System.Collections.Generic.List<System.Tuple<Mono.Cecil.FieldReference, Mono.Cecil.TypeReference>>;

namespace Unity.ZeroPlayer
{
    class InterfaceGen
    {
        // Duplicated from Unity.Jobs.LowLevel
        public enum JobType
        {
            Single,
            ParallelFor
        }

        // Note that these can use nameof(Type.Func) when we switch to the ILPP approach.
        // Placed on the IJobBase instance:
        const string PrepareJobAtScheduleTimeFn = "PrepareJobAtScheduleTimeFn_Gen";
        const string PrepareJobAtExecuteTimeFn = "PrepareJobAtExecuteTimeFn_Gen";
        const string CleanupJobFn = "CleanupJobFn_Gen";
        const string GetExecuteMethodFn = "GetExecuteMethod_Gen";
        const string GetUnmanagedJobSizeFn = "GetUnmanagedJobSize_Gen";
        const string GetMarshalMethodFn = "GetMarshalMethod_Gen";

        // Placed on the JobProducer
        const string ProducerExecuteFn = "ProducerExecuteFn_Gen";
        const string ProducerCleanupFn = "ProducerCleanupFn_Gen"; // Only for IJobParallel for.

        const int EXECUTE_JOB_PARAM = 0;
        const int EXECUTE_JOB_INDEX_PARAM = 4;

        List<AssemblyDefinition> m_Assemblies;
        AssemblyDefinition m_SystemAssembly;
        AssemblyDefinition m_ZeroJobsAssembly;
        AssemblyDefinition m_LowLevelAssembly;
        TypeDefinition m_AtomicDef; // if null, then a release build (no safety handles)
        TypeDefinition m_DisposeSentinelDef; // if null, then a release build (no safety handles)
        MethodDefinition m_ScheduleJob;
        MethodDefinition m_ExecuteJob;
        MethodDefinition m_CleanupJob;
        TypeDefinition m_IJobBase;
        TypeDefinition m_UnsafeUtilityDef;
        TypeDefinition m_JobUtilityDef;
        TypeDefinition m_PinvokeCallbackAttribute;
        TypeDefinition m_JobMetaDataDef;
        TypeDefinition m_JobRangesDef;
        // IJobForEach re-uses types, and we don't want to multiply patch. This just records work done so it isn't re-done.
        HashSet<string> m_Patched = new HashSet<string>();

        // TODO the JobDataField is truly strange, and I'd like to pull it out.
        // But there's working code that uses it, so it isn't trivial.
        // A better generic resolution system might fix it.

        public class JobDesc
        {
            // Type of the producer: CustomJobProcess.
            public TypeReference JobProducer;
            // Just the Resolve() of above; use all the time.
            public TypeDefinition JobProducerDef;
            // Type of the job: ICustomJob
            public TypeReference JobInterface;
            // Type of the JobData, which is the first parameter of
            // the Execute: CustomJobData<T>
            // (Where T, remember, is an ICustomJob)
            public TypeReference JobData;
            // Single or Parallel
            public JobType JobType;
            // If the jobs wraps an inner definition, it is here. (Or null if not.)
            public FieldDefinition JobDataField;
            // If the job is IJobForEach, its Inferred data. IJobForEach aliases different structure
            // types on top of one other, and other very specialized behaviors.
            public TypeReference JobDataInferred;
        }

        public List<JobDesc> jobList = new List<JobDesc>();
        public List<TypeDefinition> typesToMakePublic = new List<TypeDefinition>();

        // Performs the many assorted tasks to allow Jobs (Custom, Unity, etc.)
        // to run without reflection. The name refers to creating the IJobBase
        // interface (and code-gen of the appropriate methods) for all Jobs.
        public InterfaceGen(List<AssemblyDefinition> assemblies)
        {
            m_Assemblies = assemblies;
            m_SystemAssembly = assemblies.First(asm => asm.Name.Name == "mscorlib");
            m_ZeroJobsAssembly = assemblies.First(asm => asm.Name.Name == "Unity.ZeroJobs");
            m_LowLevelAssembly = assemblies.First(asm => asm.Name.Name == "Unity.LowLevel");

            // Unity.Collections.LowLevel.Unsafe.AtomicSafetyHandle
            m_AtomicDef = m_ZeroJobsAssembly.MainModule.Types.FirstOrDefault(i =>
                i.FullName == "Unity.Collections.LowLevel.Unsafe.AtomicSafetyHandle");
            m_DisposeSentinelDef = m_ZeroJobsAssembly.MainModule.Types.FirstOrDefault(i =>
                i.FullName == "Unity.Collections.LowLevel.Unsafe.DisposeSentinel");
            m_UnsafeUtilityDef = m_LowLevelAssembly.MainModule.GetAllTypes().First(i =>
                i.FullName == "Unity.Collections.LowLevel.Unsafe.UnsafeUtility");

            m_JobUtilityDef = m_ZeroJobsAssembly.MainModule.Types.First(i =>
                i.FullName == "Unity.Jobs.LowLevel.Unsafe.JobsUtility");
            m_PinvokeCallbackAttribute = m_ZeroJobsAssembly.MainModule.Types.First(i =>
                i.FullName == "Unity.Jobs.MonoPInvokeCallbackAttribute");
            m_JobMetaDataDef = m_ZeroJobsAssembly.MainModule.Types.First(i =>
                i.FullName == "Unity.Jobs.LowLevel.Unsafe.JobMetaData");
            m_JobRangesDef = m_ZeroJobsAssembly.MainModule.Types.First(i =>
                i.FullName == "Unity.Jobs.LowLevel.Unsafe.JobRanges");

            FindAllJobProducers();
        }

        // TODO: move to some general utility place. Here to package issue for release.
        public static void IterateFields(TypeReference type, Action<FieldTypeTuple> processFunc)
        {
            var hierarchy = new FieldTypeTuple();
            IterateFieldsRecurse(type, hierarchy, processFunc);
        }

        public static void IterateFieldsRecurse(TypeReference type, FieldTypeTuple hierarchy, Action<FieldTypeTuple> processFunc)
        {
            var typeResolver = TypeResolver.For(type);

            foreach (var f in type.Resolve().Fields)
            {
                var fieldReference = typeResolver.Resolve(f);
                var fieldType = typeResolver.Resolve(f.FieldType);

                // Early out the statics: (may add an option to change this later. But see next comment.)
                if (f.IsStatic)
                    continue;

                hierarchy.Add(new Tuple<FieldReference, TypeReference>(fieldReference, fieldType));
                processFunc(hierarchy);

                // Excluding statics for recursion covers:
                // 1) enums which infinitely recurse because the values in the enum are of the same enum type
                // 2) statics which infinitely recurse themselves (Such as vector3.zero.zero.zero.zero)
                if (fieldType.IsValueType && !fieldType.IsPrimitive)
                {
                    IterateFieldsRecurse(fieldType, hierarchy, processFunc);
                }

                hierarchy.RemoveAt(hierarchy.Count - 1);
            }
        }

        public static TypeReference CreateImportedType(ModuleDefinition module, TypeReference type)
        {
            if (type.IsGenericInstance)
            {
                var importedType = new GenericInstanceType(module.ImportReference(type.Resolve()));
                var genericType = type as GenericInstanceType;
                foreach (var ga in genericType.GenericArguments)
                    importedType.GenericArguments.Add(ga.IsGenericParameter ? ga : module.ImportReference(ga));
                return module.ImportReference(importedType);
            }
            return module.ImportReference(type);
        }

        public static FieldReference CreateImportedType(ModuleDefinition module, FieldReference fieldRef)
        {
            var declaringType = CreateImportedType(module, fieldRef.DeclaringType);
            var fieldType = CreateImportedType(module, fieldRef.FieldType);
            var importedField = new FieldReference(fieldRef.Name, fieldType, declaringType);
            return module.ImportReference(importedField);
        }

        JobType FindJobType(TypeDefinition producer)
        {
            foreach (var m in producer.Methods)
            {
                if (m.HasBody)
                {
                    var bc = m.Body.Instructions;
                    for (int i = 0; i < bc.Count; i++)
                    {
                        if (bc[i].OpCode == OpCodes.Call && ((MethodReference)bc[i].Operand).Name ==
                            "CreateJobReflectionData")
                        {
                            // Found the call to CreateJobReflection data. Now look at the constant
                            // on the stack to get the Single (0) or Parallel (1)
                            int j = i - 1;
                            while (j > 0)
                            {
                                if (bc[j].OpCode == OpCodes.Ldc_I4_0) return JobType.Single;
                                if (bc[j].OpCode == OpCodes.Ldc_I4_1) return JobType.ParallelFor;
                                --j;
                            }

                            throw new Exception($"The CreateJobReflectionData in method '{m.Name}' on '{producer.Name}' does not specify a constant value for JobType.");
                        }
                    }
                }
            }

            throw new Exception($"Can not find the CreateJobReflectionData call on '{producer.Name}'");
        }

        // Scans all the JobProducers and fills in the JobDesc that gives information about them.
        void FindAllJobProducers()
        {
            foreach (var asm in m_Assemblies)
            {
                foreach (TypeDefinition type in asm.MainModule.GetAllTypes())
                {
                    if (!type.IsInterface || !type.HasCustomAttributes)
                        continue;

                    CustomAttribute ca = GetProducerAttributeIfExists(type);

                    if (ca == null)
                        continue;

                    TypeReference producer = (TypeReference)ca.ConstructorArguments[0].Value;

                    // There can be multiple Execute methods; simple check to find the required one.
                    // The required form:
                    //  public delegate void ExecuteJobFunction(ref JobStruct<T> jobStruct, System.IntPtr additionalPtr, System.IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex);
                    var executeMethod = producer.Resolve().Methods.FirstOrDefault(n =>
                        n.Name == "Execute"
                        && n.Parameters.Count == 5
                        && n.Parameters[1].ParameterType.MetadataType == MetadataType.IntPtr
                        && n.Parameters[2].ParameterType.MetadataType == MetadataType.IntPtr
                        && n.Parameters[3].ParameterType.Name == "JobRanges&");

                    if (executeMethod == null)
                        throw new Exception($"{producer.FullName} is a JobProducer, but has no valid Execute method.");

                    JobDesc jobDesc = new JobDesc();
                    jobDesc.JobProducer = producer;
                    jobDesc.JobProducerDef = producer.Resolve();
                    jobDesc.JobInterface = type;

                    TypeReference jobData = executeMethod.Parameters[EXECUTE_JOB_PARAM].ParameterType.GetElementType();

                    const string jobForEachPrefix = "Unity.Entities.JobForEachExtensions/JobStruct_Process";
                    bool isIJobForEach = jobData.FullName.StartsWith(jobForEachPrefix);
                    CustomAttribute inferredAttribute = GetInferredAttributeIfExists(type);
                    if (isIJobForEach && inferredAttribute == null)
                    {
                        // Unused (by code-gen) definitions. There is a very similar one that will be used later.
                        continue;
                    }

                    if (inferredAttribute != null)
                    {
                        jobDesc.JobDataInferred = (TypeReference)inferredAttribute.ConstructorArguments[0].Value;
                    }

                    jobDesc.JobData = jobData;
                    jobDesc.JobDataField = FindJobData(jobDesc.JobData.Resolve());
                    if (jobDesc.JobDataField == null)
                    {
                        jobDesc.JobData = producer;
                    }
                    else
                    {
                        typesToMakePublic.Add(jobDesc.JobDataField.DeclaringType);
                    }

                    // https://unity3d.atlassian.net/browse/DOTSR-498
                    // All IJobForEach run in Single mode.
                    if (jobDesc.JobDataInferred != null)
                        jobDesc.JobType = JobType.Single; // Force ForEach to Single.
                    else
                        jobDesc.JobType = FindJobType(producer.Resolve());
                    jobList.Add(jobDesc);
                }
            }
        }

        static CustomAttribute GetProducerAttributeIfExists(TypeDefinition type)
        {
            return type.CustomAttributes.FirstOrDefault(a =>
                a.AttributeType.FullName == "Unity.Jobs.LowLevel.Unsafe.JobProducerTypeAttribute");
        }

        static CustomAttribute GetInferredAttributeIfExists(TypeDefinition type)
        {
            return type.CustomAttributes.FirstOrDefault(a =>
                a.AttributeType.FullName == "Unity.Jobs.LowLevel.Unsafe.JobInferredTypeAttribute");
        }

        FieldDefinition FindJobData(TypeDefinition tr)
        {
            if (tr == null)
                return null;

            // internal struct JobStruct<T> where T : struct, IJob
            // {
            //    static IntPtr JobReflectionData;
            //    internal T JobData;                    <---- looking for this. Has the same name as the first generic.
            //
            // But some (many) jobs don't have the inner JobData; the job itself is the type.
            // So need to handle that fallback.

            return tr.Fields.FirstOrDefault(f => f.FieldType.Name == tr.GenericParameters[0].Name);
        }

        // Generates the ProducerExecuteFn which wraps the user Execute, to
        // pass down job data structure.
        void GenerateProducerExecuteFn(ModuleDefinition module, JobDesc jobDesc)
        {
            var intPtr = m_SystemAssembly.MainModule.Types.First(i => i.FullName == "System.IntPtr");
            var intPtrCtor = intPtr.GetConstructors().First(c => c.Parameters.Count == 1 && c.Parameters[0].ParameterType.FullName == "System.Int32");
            var freeRef = m_UnsafeUtilityDef.Methods.First(n => n.Name == "Free");
            /*
             * types from other assemblies need to be able to reach into this type and grab its generated execute method
             * via GetExecuteMethod_Gen(), so it has to be public. 
             */
            jobDesc.JobProducerDef.IsPublic = true;
            if (jobDesc.JobProducerDef.IsNested)
                jobDesc.JobProducerDef.IsNestedPublic = true;

            MethodDefinition executeMethod = jobDesc.JobProducerDef.Methods.First(m => m.Name == "Execute");
            MethodDefinition executeGen = new MethodDefinition(ProducerExecuteFn,
                MethodAttributes.Public | MethodAttributes.Static,
                module.ImportReference(typeof(void)));

            var pInvokeCctor = m_PinvokeCallbackAttribute.GetConstructors();
            executeGen.CustomAttributes.Add(new CustomAttribute(module.ImportReference(pInvokeCctor.First())));

            var metaPtrParam = new ParameterDefinition("jobMetaPtr", ParameterAttributes.None,
                module.ImportReference(typeof(void*)));
            executeGen.Parameters.Add(metaPtrParam);

            var jobIndexParam = new ParameterDefinition("jobIndex", ParameterAttributes.None,
                module.ImportReference(typeof(int)));
            executeGen.Parameters.Add(jobIndexParam);

            // TODO: is it safe to not copy the structure in MT??
            // var genericJobDataRef = jobDesc.JobData.MakeGenericInstanceType(jobDesc.JobData.GenericParameters.ToArray());
            // var jobData = new VariableDefinition(module.ImportReference(genericJobDataRef));
            // executeRT.Body.Variables.Add(jobData);

            var jobDataPtr = new VariableDefinition(module.ImportReference(typeof(void*)));
            executeGen.Body.Variables.Add(jobDataPtr);

            var stackJobRange = new VariableDefinition(module.ImportReference(m_JobRangesDef));
            executeGen.Body.Variables.Add(stackJobRange);

            executeGen.Body.InitLocals = true;
            var il = executeGen.Body.GetILProcessor();

            // TODO: is it safe to not copy the structure in MT??
            // CustomJobData<T> jobData = *ptr;
            // bc.Add(Instruction.Create(OpCodes.Ldarg, ptrParam));
            // bc.Add(Instruction.Create(OpCodes.Ldobj, module.ImportReference(genericJobDataRef)));
            // bc.Add(Instruction.Create(OpCodes.Stloc, jobData));

            // void* jobDataPtr = jobMetaPtr + sizeof(JobMetaData);
            il.Emit(OpCodes.Ldarg, metaPtrParam);
            il.Emit(OpCodes.Sizeof, module.ImportReference(m_JobMetaDataDef));
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, jobDataPtr);

            // The JobRanges are needed *per thread*. GetWorkStealingRange will modify the JobRanges
            // as it does work. Not obvious: the JobRanges are stored as the first thing in the metaData,
            // so the metaDataPtr is a pointer to the jobRanges.
            // Make a stack copy:
            il.Emit(OpCodes.Ldarg, metaPtrParam);
            il.Emit(OpCodes.Ldobj, module.ImportReference(m_JobRangesDef));
            il.Emit(OpCodes.Stloc, stackJobRange);

            // Execute(ref jobData, new IntPtr(0), new IntPtr(0), ref ranges, 0);
            il.Emit(OpCodes.Ldloc, jobDataPtr);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newobj, module.ImportReference(intPtrCtor));
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newobj, module.ImportReference(intPtrCtor));

            il.Emit(OpCodes.Ldloca, stackJobRange);
            il.Emit(OpCodes.Ldarg, jobIndexParam);
            il.Emit(OpCodes.Call,
                module.ImportReference(
                    executeMethod.MakeHostInstanceGeneric(jobDesc.JobData.GenericParameters.ToArray())));

            if (jobDesc.JobType == JobType.Single)
            {
                // UnsafeUtility.Free(structPtr, Allocator.TempJob);
                il.Emit(OpCodes.Ldarg, metaPtrParam);
                il.Emit(OpCodes.Ldc_I4_3); // literal value of Allocator.TempJob
                il.Emit(OpCodes.Call, module.ImportReference(freeRef));
            }

            il.Emit(OpCodes.Ret);
            jobDesc.JobProducerDef.Methods.Add(executeGen);
        }

        // Generates the method to cleanup memory and call the IJobBase.CleanupJobFn_Gen()
        void GenerateProducerCleanupFn(ModuleDefinition module, JobDesc jobDesc)
        {
            MethodDefinition cleanupFn = new MethodDefinition(ProducerCleanupFn,
                MethodAttributes.Public | MethodAttributes.Static,
                module.ImportReference(typeof(void)));

            var pInvokeCctor = m_PinvokeCallbackAttribute.GetConstructors();
            cleanupFn.CustomAttributes.Add(new CustomAttribute(module.ImportReference(pInvokeCctor.First())));

            var freeDef = m_UnsafeUtilityDef.Methods.First(n => n.Name == "Free");

            var metaPtrParam = new ParameterDefinition("jobMetaPtr", ParameterAttributes.None,
                module.ImportReference(typeof(void*)));
            cleanupFn.Parameters.Add(metaPtrParam);

            var jobDataPtr = new VariableDefinition(module.ImportReference(typeof(void*)));
            cleanupFn.Body.Variables.Add(jobDataPtr);

            cleanupFn.Body.InitLocals = true;
            var il = cleanupFn.Body.GetILProcessor();

            // void* ptr = jobMetaPtr + sizeof(JobMetaData);
            il.Emit(OpCodes.Ldarg, metaPtrParam);
            il.Emit(OpCodes.Sizeof, module.ImportReference(m_JobMetaDataDef));
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, jobDataPtr);

            // The UserJobData case is tricky.
            // jobData.UserJobData.CleanupTasksFn_Gen()
            // OR
            // jobData.CleanupTasksFn_Gen()
            VariableDefinition jobDataVar;
            if (jobDesc.JobDataField != null)
            {
                var genericJobDataRef = jobDesc.JobData.MakeGenericInstanceType(jobDesc.JobData.GenericParameters.ToArray());
                jobDataVar = new VariableDefinition(module.ImportReference(genericJobDataRef));
                cleanupFn.Body.Variables.Add(jobDataVar);

                // CustomJobData<T> jobData = *ptr;
                il.Emit(OpCodes.Ldloc, jobDataPtr);
                il.Emit(OpCodes.Ldobj, module.ImportReference(genericJobDataRef));
                il.Emit(OpCodes.Stloc, jobDataVar);

                // jobData.UserJobData.CleanupTasksFn_Gen(ptr)
                il.Emit(OpCodes.Ldloca, jobDataVar);
                il.Emit(OpCodes.Ldflda,
                    module.ImportReference(TypeRegGen.MakeGenericFieldSpecialization(jobDesc.JobDataField, jobDesc.JobData.GenericParameters.ToArray())));

                il.Emit(OpCodes.Ldloc, jobDataPtr);
                il.Emit(OpCodes.Constrained, jobDesc.JobProducerDef.GenericParameters[0]);
            }
            else
            {
                var jobDataRef = module.ImportReference(jobDesc.JobData.GenericParameters[0]);
                jobDataVar = new VariableDefinition(jobDataRef);
                cleanupFn.Body.Variables.Add(jobDataVar);

                // T jobData = *ptr;
                il.Emit(OpCodes.Ldloc, jobDataPtr);
                il.Emit(OpCodes.Ldobj, jobDataRef);
                il.Emit(OpCodes.Stloc, jobDataVar);

                // jobData.CleanupTasksFn_Gen(null)
                // There is no wrapping data structure; so the parameter can be null.
                il.Emit(OpCodes.Ldloca, jobDataVar);

                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Conv_U);
                il.Emit(OpCodes.Constrained, jobDataRef);
            }

            // The first generic parameter is always the user Job, which is where the IJobBase has been attached.
            il.Emit(OpCodes.Callvirt, module.ImportReference(m_CleanupJob));

            if (jobDesc.JobDataField != null)
            {
                GenWrapperDeallocateIL(cleanupFn, module.Assembly, jobDesc.JobData.Resolve(), jobDataVar);
            }

            // UnsafeUtility.Free(metaPtrParam, Allocator.TempJob);
            il.Emit(OpCodes.Ldarg, metaPtrParam);
            il.Emit(OpCodes.Ldc_I4_3); // literal value of Allocator.TempJob
            il.Emit(OpCodes.Call, module.ImportReference(freeDef));

            il.Emit(OpCodes.Ret);

            jobDesc.JobProducerDef.Methods.Add(cleanupFn);
        }

        // Adds the prefix and postfix calls to the Execute method.
        // For a super-simple Execute:
        // public static void Execute(...
        // {
        //      jobData.UserJobData.PrepareJobAtExecuteTimeFn_Gen(jobIndex);  <-- generated here
        //      jobData.UserJobData.Execute(ref jobData.abData);
        //      jobData.UserJobData.CleanupJobFn_Gen(&jobData);               <-- generated here
        // }
        void PatchProducerExecute(ModuleDefinition module, JobDesc jobDesc)
        {
            MethodDefinition executeMethod = jobDesc.JobProducerDef.Methods.First(m => m.Name == "Execute");

            var bc = executeMethod.Body.Instructions;

            var il = executeMethod.Body.GetILProcessor();
            var first = bc[0];
            var last = bc[bc.Count - 1];

            il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
            if (jobDesc.JobDataField != null)
                il.InsertBefore(first, Instruction.Create(OpCodes.Ldflda,
                    module.ImportReference(TypeRegGen.MakeGenericFieldSpecialization(jobDesc.JobDataField,
                        jobDesc.JobData.GenericParameters.ToArray()))));
            il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg, executeMethod.Parameters[EXECUTE_JOB_INDEX_PARAM]));

            // The first generic parameter is always the user Job, which is where the IJobBase has been attached.
            il.InsertBefore(first, Instruction.Create(OpCodes.Constrained, jobDesc.JobProducerDef.GenericParameters[0]));
            il.InsertBefore(first, Instruction.Create(OpCodes.Callvirt, module.ImportReference(m_ExecuteJob)));

            if (jobDesc.JobType == JobType.Single)
            {
                il.InsertBefore(last, Instruction.Create(OpCodes.Ldarg_0));
                if (jobDesc.JobDataField != null)
                    il.InsertBefore(last, Instruction.Create(OpCodes.Ldflda,
                        module.ImportReference(
                            TypeRegGen.MakeGenericFieldSpecialization(jobDesc.JobDataField, jobDesc.JobData.GenericParameters.ToArray()))));

                if (jobDesc.JobDataField != null)
                {
                    // pass in the wrapper.
                    il.InsertBefore(last, Instruction.Create(OpCodes.Ldarg_0));
                }
                else
                {
                    // pass in null
                    il.InsertBefore(last, Instruction.Create(OpCodes.Ldc_I4_0));
                    il.InsertBefore(last, Instruction.Create(OpCodes.Conv_U));
                }

                // The first generic parameter is always the user Job, which is where the IJobBase has been attached.
                il.InsertBefore(last, Instruction.Create(OpCodes.Constrained, jobDesc.JobProducerDef.GenericParameters[0]));
                il.InsertBefore(last, Instruction.Create(OpCodes.Callvirt, module.ImportReference(m_CleanupJob)));
            }
        }

        static TypeReference[] MakeGenericArgsArray(ModuleDefinition module, IGenericParameterProvider forType, IEnumerable<GenericParameter> gp)
        {
            List<TypeReference> lst = new List<TypeReference>();
            foreach (var g in gp)
            {
                TypeReference t = module.ImportReference(g);
                lst.Add(t);

                // We may have more generic parameters than we need. For example,
                // the schedule may take more parameters than needed by the job.
                if (lst.Count == forType.GenericParameters.Count)
                    break;
            }

            return lst.ToArray();
        }

        // Patches the Schedule method to add the size, and call the IJobBase.PrepareJobAtScheduleTimeFn_Gen
        void PatchJobSchedule(ModuleDefinition module, JobDesc jobDesc)
        {
            TypeDefinition parent = jobDesc.JobProducerDef.DeclaringType ?? jobDesc.JobProducerDef;

            foreach (var method in parent.Methods)
            {
                if (method.Body?.Instructions != null)
                {
                    if (m_Patched.Contains("PatchJobSchedule" + method.FullName))
                        continue;
                    m_Patched.Add("PatchJobSchedule" + method.FullName);

                    var bc = method.Body.Instructions;
                    Instruction lastProcessed = null;
                    for (int i = 0; i < bc.Count; ++i)
                    {
                        if (bc[i].OpCode == OpCodes.Call)
                        {
                            if (((MethodReference)bc[i].Operand).FullName.Contains(
                                "Unity.Jobs.LowLevel.Unsafe.JobsUtility/JobScheduleParameters::.ctor"))
                            {
                                if (ReferenceEquals(bc[i], lastProcessed))
                                    continue;
                                lastProcessed = bc[i];

                                const int kSizeOffset = 2;
                                const int kPrepareOffset = 1;

                                if (bc[i - kSizeOffset].OpCode != OpCodes.Ldc_I4_0)
                                    throw new Exception(
                                        $"Expected to find default 0 value for size in JobScheduleParameters when processing '{method.FullName}'");

                                // the 3 here is a magic flag value from the default parameter to help find the byte code.
                                if (bc[i - kPrepareOffset].OpCode != OpCodes.Ldc_I4_3)
                                    throw new Exception(
                                        $"Unexpected default value in '{method.FullName}'");

                                // Patch the size argument into a SizeOf
                                // Note this replaces one bytecode with another, so we haven't mutated the array/list
                                // we're working on.
                                {
                                    if (jobDesc.JobDataField != null)
                                    {
                                        var arr = MakeGenericArgsArray(module, jobDesc.JobData, method.GenericParameters);
                                        TypeReference td = null;
                                        if (jobDesc.JobDataInferred != null)
                                            td = module.ImportReference(jobDesc.JobDataInferred.MakeGenericInstanceType(arr));
                                        else
                                            td = module.ImportReference(jobDesc.JobData.MakeGenericInstanceType(arr));
                                        bc[i - kSizeOffset] = Instruction.Create(OpCodes.Sizeof, module.ImportReference(td));
                                    }
                                    else
                                    {
                                        bc[i - kSizeOffset] = Instruction.Create(OpCodes.Sizeof, method.Parameters[0].ParameterType);
                                    }
                                }

                                // The jobData can be a local or a parameter; go find it.
                                ParameterDefinition jobDataParam = null;
                                VariableDefinition jobDataVar = null;
                                {
                                    // The parameter to AddressOf() is the parameter or local we want to load.
                                    for (int j = i - 1; j > 0; --j)
                                    {
                                        if (bc[j].OpCode == OpCodes.Call &&
                                            ((MethodReference)bc[j].Operand).Name == "AddressOf")
                                        {
                                            if (bc[j - 1].OpCode == OpCodes.Ldarga || bc[j - 1].OpCode == OpCodes.Ldarga_S)
                                            {
                                                jobDataParam = (ParameterDefinition)bc[j - 1].Operand;
                                            }
                                            else
                                            {
                                                jobDataVar = (VariableDefinition)bc[j - 1].Operand;
                                            }

                                            break;
                                        }
                                    }

                                    if (jobDataParam == null && jobDataVar == null)
                                        throw new ArgumentException($"Expected to find AddressOf call in JobSchedule parameters while looking at `{method.FullName}'");
                                }

                                // Patching the last argument into a call to PrepareJobAtScheduleTimeFn_Gen()
                                {
                                    // Add new instructions before the call:
                                    Instruction callInstruction = bc[i];
                                    // Destroy the load of the const (will become the call to PrepareJobAtScheduleTimeFn_Gen())
                                    bc[i - kPrepareOffset] = Instruction.Create(OpCodes.Nop);
                                    ILProcessor il = method.Body.GetILProcessor();

                                    // data.UserJobData.PrepareJobAtScheduleTimeFn_Gen()
                                    // OR
                                    // data.PrepareJobAtScheduleTimeFn_Gen()
                                    if (jobDesc.JobDataInferred != null || jobDesc.JobDataField == null)

                                    {
                                        if(method.Parameters[0].ParameterType.IsByReference)
                                            il.InsertBefore(callInstruction, Instruction.Create(OpCodes.Ldarg, method.Parameters[0]));
                                        else
                                            il.InsertBefore(callInstruction, Instruction.Create(OpCodes.Ldarga, method.Parameters[0]));
                                    }
                                    else
                                    {
                                        if (jobDataParam != null)
                                        {
                                            il.InsertBefore(callInstruction, Instruction.Create(OpCodes.Ldarga, jobDataParam));
                                        }
                                        else
                                        {
                                            il.InsertBefore(callInstruction, Instruction.Create(OpCodes.Ldloca, jobDataVar));
                                        }

                                        TypeDefinition userDataFD = jobDesc.JobDataField.DeclaringType;
                                        var arr = MakeGenericArgsArray(module, userDataFD, method.GenericParameters);

                                        il.InsertBefore(callInstruction,
                                            Instruction.Create(OpCodes.Ldflda,
                                                module.ImportReference(
                                                    TypeRegGen.MakeGenericFieldSpecialization(jobDesc.JobDataField, arr))));
                                    }

                                    // The first generic parameter is always the user Job, which is where the IJobBase has been attached.
                                    TypeReference constraintTypeRef = module.ImportReference(method.GenericParameters[0]);

                                    il.InsertBefore(callInstruction,
                                        Instruction.Create(OpCodes.Constrained, constraintTypeRef));

                                    il.InsertBefore(callInstruction,
                                        Instruction.Create(OpCodes.Callvirt, module.ImportReference(m_ScheduleJob)));
                                }
                            }
                        }
                    }
                }
            }
        }

        // Patch CreateJobReflectionData to pass in the ProducerExecuteFn_Gen and ProducerCleanupFn_Gen methods.
        void PatchCreateJobReflection(ModuleDefinition module, JobDesc jobDesc)
        {
            var managedJobDelegate = m_JobUtilityDef.NestedTypes.First(i => i.Name == "ManagedJobDelegate");
            var managedJobDelegateCtor = managedJobDelegate.Methods[0];
            var managedForEachJobDelegate = m_JobUtilityDef.NestedTypes.First(i => i.Name == "ManagedJobForEachDelegate");
            var managedForEachJobDelegateCtor = managedForEachJobDelegate.Methods[0];
            var managedJobMarshalDelegate = m_JobUtilityDef.NestedTypes.First(i => i.Name == "ManagedJobMarshalDelegate");
            var managedJobMarshalDelegateCtor = module.ImportReference(managedJobMarshalDelegate.Methods[0]);

            var genExecuteMethodFnRef = module.ImportReference(m_IJobBase.Methods.First(m => m.Name == GetExecuteMethodFn));
            var genUnmanagedJobSizeFnRef = module.ImportReference(m_IJobBase.Methods.First(m => m.Name == GetUnmanagedJobSizeFn));
            var genMarshalMethodFnRef = module.ImportReference(m_IJobBase.Methods.First(m => m.Name == GetMarshalMethodFn));

            // Patch the CreateJobReflectionData to pass in ExecuteRT_Gen
            foreach (var method in jobDesc.JobProducerDef.Methods)
            {
                if (m_Patched.Contains("PatchCreateJobReflection" + method.FullName))
                    continue;
                m_Patched.Add("PatchCreateJobReflection" + method.FullName);

                var bc = method.Body.Instructions;
                Instruction lastProcessed = null;
                for (int i = 0; i < bc.Count; ++i)
                {
                    if (bc[i].OpCode == OpCodes.Call && bc[i].Operand is MethodReference
                        && (bc[i].Operand as MethodReference).FullName.StartsWith(
                            "System.IntPtr Unity.Jobs.LowLevel.Unsafe.JobsUtility::CreateJobReflectionData")
                    )
                    {
                        if (ReferenceEquals(bc[i], lastProcessed))
                            continue;
                        lastProcessed = bc[i];

                        var typeOfUserJobStruct = jobDesc.JobProducerDef.GenericParameters[0];
                        typeOfUserJobStruct.Constraints.Add(new GenericParameterConstraint(module.ImportReference(m_IJobBase)));

                        var userJobStructLocal = new VariableDefinition(typeOfUserJobStruct);
                        method.Body.Variables.Add(userJobStructLocal);

                        MethodDefinition producerExecuteMD = jobDesc.JobProducerDef.Methods.FirstOrDefault(m => m.Name == ProducerExecuteFn);
                        if (producerExecuteMD == null)
                            throw new ArgumentException($"Type '{jobDesc.JobProducerDef.FullName}' does not have a generated '{ProducerExecuteFn}' method");

                        MethodDefinition producerCleanupFn = null;
                        if (jobDesc.JobType == JobType.ParallelFor)
                        {
                            producerCleanupFn = jobDesc.JobProducerDef.Methods.First(m => m.Name == ProducerCleanupFn);
                        }

                        // Instruction before should be default arguments of null, -1, null, null
                        if (bc[i - 1].OpCode != OpCodes.Ldnull)
                            throw new InvalidOperationException($"Expected ldnull opcode (at position -1) in '{method.FullName}'");
                        if (bc[i - 2].OpCode != OpCodes.Ldc_I4_M1)
                            throw new InvalidOperationException($"Expected Ldc_I4_M1 opcode (at position -2) in '{method.FullName}'");
                        if (bc[i - 3].OpCode != OpCodes.Ldnull)
                            throw new InvalidOperationException($"Expected ldnull opcode (at position -3) in '{method.FullName}'");
                        if (bc[i - 4].OpCode != OpCodes.Ldnull)
                            throw new InvalidOperationException($"Expected ldnull opcode (at position -4) in '{method.FullName}'");

                        var il = method.Body.GetILProcessor();
                        var func = bc[i];

                        // Wipe out the default arguments
                        il.Remove(bc[i - 1]);
                        il.Remove(bc[i - 2]);
                        il.Remove(bc[i - 3]);
                        il.Remove(bc[i - 4]);

                        // and now replace with new parameters.
                        List<TypeReference> lst = new List<TypeReference>();
                        foreach (var g in jobDesc.JobProducerDef.GenericParameters)
                        {
                            TypeReference t = module.ImportReference(g);
                            lst.Add(t);
                        }

                        // Default initialize our local job struct
                        il.InsertBefore(func, Instruction.Create(OpCodes.Ldloca, userJobStructLocal));
                        il.InsertBefore(func, Instruction.Create(OpCodes.Initobj, typeOfUserJobStruct));

                        // call IJob.GetExecuteMethod_Gen()
                        il.InsertBefore(func, Instruction.Create(OpCodes.Ldloca, userJobStructLocal));
                        il.InsertBefore(func, Instruction.Create(OpCodes.Constrained, typeOfUserJobStruct));
                        il.InsertBefore(func, Instruction.Create(OpCodes.Callvirt, genExecuteMethodFnRef));

                        // ManagedJobForEachDelegate codegenCleanupDelegate
                        //
                        // null is used as the delegate value (for non-ParallelFor jobs) and is used as the delegate function 
                        // context for ParallelForJobs (hence why the ldnull is outside the if block)
                        il.InsertBefore(func, Instruction.Create(OpCodes.Ldnull));
                        if (jobDesc.JobType == JobType.ParallelFor)
                        {
                            var closedProducerCleanupFn = producerCleanupFn.MakeHostInstanceGeneric(lst.ToArray());
                            il.InsertBefore(func, Instruction.Create(OpCodes.Ldftn, module.ImportReference(closedProducerCleanupFn)));
                            il.InsertBefore(func, Instruction.Create(OpCodes.Newobj, module.ImportReference(managedJobDelegateCtor)));
                        }

                        // call IJob.GetUnmanagedJobSize_Gen()
                        il.InsertBefore(func, Instruction.Create(OpCodes.Ldloca, userJobStructLocal));
                        il.InsertBefore(func, Instruction.Create(OpCodes.Constrained, typeOfUserJobStruct));
                        il.InsertBefore(func, Instruction.Create(OpCodes.Callvirt, genUnmanagedJobSizeFnRef));

                        // call IJob.GetMarshalMethod_Gen()
                        il.InsertBefore(func, Instruction.Create(OpCodes.Ldloca, userJobStructLocal));
                        il.InsertBefore(func, Instruction.Create(OpCodes.Constrained, typeOfUserJobStruct));
                        il.InsertBefore(func, Instruction.Create(OpCodes.Callvirt, genMarshalMethodFnRef));
                    }
                }
            }
        }

        public void PatchJobsCode()
        {
            foreach (JobDesc jobDesc in jobList)
            {
                var module = jobDesc.JobInterface.Module;

                GenerateProducerExecuteFn(module, jobDesc);
                if (jobDesc.JobType == JobType.ParallelFor)
                {
                    GenerateProducerCleanupFn(module, jobDesc);
                }

                PatchProducerExecute(module, jobDesc);
                PatchJobSchedule(module, jobDesc);
                PatchCreateJobReflection(module, jobDesc);
            }
        }

        bool TypeHasIJobBase(TypeDefinition td)
        {
            return td.IsStructWithInterface("Unity.Jobs.IJobBase");
        }

        bool TypeHasIJobBaseMethods(TypeDefinition td)
        {
            if (td.HasMethods && td.Methods.FirstOrDefault(m => m.Name == PrepareJobAtExecuteTimeFn) != null)
                return true;
            return false;
        }

        public void InjectBurstInfrastructureMethods()
        {
            foreach (var asm in m_Assemblies)
            {
                var allTypes = asm.MainModule.GetAllTypes();
                foreach (var type in allTypes)
                {
                    if (type.IsStructWithInterface("Unity.Jobs.IJobBase"))
                    {
                        bool found = false;
                        List<TypeReference> args = new List<TypeReference> { type };

                        if (type.IsStructWithInterface("Unity.Entities.JobForEachExtensions/IBaseJobForEach"))
                        {
                            for (int i = 0; i < type.Interfaces.Count; i++)
                            {
                                foreach (JobDesc job in jobList)
                                {
                                    // For IJobForEach, pull of the generic part of the name by using GetElementType().
                                    // We can match on the name which includes the pattern. (_ECC for example.)
                                    if (type.Interfaces[i].InterfaceType.GetElementType().FullName == job.JobInterface.GetElementType().FullName)
                                    {
                                        // Find the Execute method, pull out the types. A full set of types
                                        // is needed to close the Execute method.
                                        MethodDefinition executeMethod = type.Methods.First(f => f.Name == "Execute");
                                        List<TypeReference> types = ExecuteGen.ForEachExecuteTypes(executeMethod, out _);
                                        args.AddRange(types);

                                        var producerExecuteFn = job.JobProducerDef.Methods.First(m => m.Name == ProducerExecuteFn);
                                        type.Methods.Add(GenGetExecuteMethodMethod(asm, type, producerExecuteFn, args));
			                            type.Methods.Add(GenGetUnmanagedJobSizeMethodMethod(asm, type));
			                            type.Methods.Add(GenGetMarshalMethodMethod(asm, type));
										
                                        found = true;
                                        break;
                                    }
                                }
                            }
                        }
                        else
                        {
                            for (int i = 0; i < type.Interfaces.Count; i++)
                            {
                                foreach (JobDesc job in jobList)
                                {
                                    if (type.Interfaces[i].InterfaceType.FullName == job.JobInterface.FullName)
                                    {
                                        var producerExecuteFn = job.JobProducerDef.Methods.First(m => m.Name == ProducerExecuteFn);
                                        type.Methods.Add(GenGetExecuteMethodMethod(asm, type, producerExecuteFn, args));
                                        type.Methods.Add(GenGetUnmanagedJobSizeMethodMethod(asm, type));
                                        type.Methods.Add(GenGetMarshalMethodMethod(asm, type));
										
										found = true;
										break;
                                    }
                                }
                            }
                        }

                        if (!found) throw new Exception($"Could not match job {type.FullName} to a known job interface.");
                    }
                }
            }
        }

        public void AddMethods()
        {
            // Find IJobBase
            //     Add SetupJob(int)
            // Patch Unity.Jobs.LowLevel.Unsafe.JobsUtility.SetupJob/Cleanup
            // Find all implementers of IJobBase
            //    Add SetupJob(int) { return }

            m_IJobBase = m_ZeroJobsAssembly.MainModule.GetAllTypes().First(i => i.FullName == "Unity.Jobs.IJobBase");
            m_IJobBase.IsPublic = true;

            m_ScheduleJob = m_IJobBase.Methods.First(m => m.Name == PrepareJobAtScheduleTimeFn);
            m_ExecuteJob = m_IJobBase.Methods.First(m => m.Name == PrepareJobAtExecuteTimeFn);
            m_CleanupJob = m_IJobBase.Methods.First(m => m.Name == CleanupJobFn);

            // Add the IJobBase interface to the custom job interface
            foreach (JobDesc job in jobList)
            {
                TypeDefinition type = ((TypeDefinition)job.JobInterface);
                type.Interfaces.Add(new InterfaceImplementation(type.Module.ImportReference(m_IJobBase)));
            }

            // Go through each type, and if it is targeted by a JobProducer, add the IJobBase interface,
            // as well as the IJobBase Setup/Cleanup methods.
            // Also handle the special case of IJobForEach.
            foreach (var asm in m_Assemblies)
            {
                var allTypes = asm.MainModule.GetAllTypes();
                foreach (var type in allTypes)
                {
                    if (type.IsValueType && type.HasInterfaces)
                    {
                        bool isIJobForEach = type.IsStructWithInterface("Unity.Entities.JobForEachExtensions/IBaseJobForEach");
                        JobDesc jobDesc = null;
                        if (!isIJobForEach)
                        {
                            for (int i = 0; i < type.Interfaces.Count; i++)
                            {
                                foreach (JobDesc job in jobList)
                                {
                                    if (type.Interfaces[i].InterfaceType.FullName == job.JobInterface.FullName)
                                    {
                                        jobDesc = job;
                                        break;
                                    }
                                }
                            }
                        }

                        if (isIJobForEach)
                        {
                            // Special case (for now) for IJobForEach
                            if (!TypeHasIJobBase(type))
                            {
                                type.Interfaces.Add(
                                    new InterfaceImplementation(type.Module.ImportReference(m_IJobBase)));
                            }

                            if (!TypeHasIJobBaseMethods(type))
                            {
                                type.Methods.Add(GenScheduleMethod(asm, type));
                                type.Methods.Add(GenExecuteMethod(asm, type));
                                type.Methods.Add(GenCleanupMethod(asm, type, null));
                            }
                        }
                        else if (jobDesc != null)
                        {
                            if (!TypeHasIJobBase(type))
                            {
                                type.Interfaces.Add(
                                    new InterfaceImplementation(type.Module.ImportReference(m_IJobBase)));
                            }

                            if (!TypeHasIJobBaseMethods(type))
                            {
                                type.Methods.Add(GenScheduleMethod(asm, type));
                                type.Methods.Add(GenExecuteMethod(asm, type));
                                type.Methods.Add(GenCleanupMethod(asm, type, jobDesc));
                            }
                        }
                    }
                }
            }
        }

        private MethodDefinition GenGetExecuteMethodMethod(AssemblyDefinition asm,
            TypeDefinition type,
            MethodDefinition genExecuteMethod,
            List<TypeReference> genericArgs)
        {
            var module = asm.MainModule;
            var managedJobDelegate = module.ImportReference(m_JobUtilityDef.NestedTypes.First(i => i.Name == "ManagedJobForEachDelegate").Resolve());
            var managedJobDelegateCtor = module.ImportReference(managedJobDelegate.Resolve().Methods[0]);
            var method = new MethodDefinition(GetExecuteMethodFn,
                MethodAttributes.Public |
                MethodAttributes.HideBySig |
                MethodAttributes.NewSlot |
                MethodAttributes.Virtual,
                managedJobDelegate);
            method.Body.InitLocals = true;
            var il = method.Body.GetILProcessor();

            /*
             * return "wrapper job struct, e.g. IJobExtensions.JobStruct<YourSpecificJobType>".ProducerExecuteFn_Gen;
             */

            il.Emit(OpCodes.Ldnull);
            /*
             * the clr will complain if we try to load a type with an unimplemented method on it, so generate one that
             * throws an exception.
             *
             * (il2cpp will explode if we try to just return one with null in it)
             */
            if (genExecuteMethod == null)
            {
                il.Emit(OpCodes.Throw);
            }
            else
            {
                TypeReference job = type;
                if (job.HasGenericParameters)
                {
                    job = job.MakeGenericInstanceType(job.GenericParameters.Select(p => job.Module.ImportReference(p)).ToArray());
                    // We just closed our own type, which is also the first element of the generic array.
                    genericArgs[0] = job;
                }

                // The generic args coming in are from the Execute method signature, but for purposes of adding generic params
                // to our ExecuteMethod itself, we only care about the concrete type, not whether the argument was passed by
                // reference or not since leaving the arguments as byreference will invalide the generic signature for the  method
                for(int i = 0; i < genericArgs.Count; ++i)
                {
                    var ga = genericArgs[i];
                    if (ga.FullName.StartsWith("Unity.Entities.DynamicBuffer"))
                    {
                        var gi = ga as GenericInstanceType;
                        genericArgs[i] = module.ImportReference(gi.GenericArguments[0].Resolve());
                    }
                    else if (ga.IsByReference)
                    {
                        genericArgs[i] = module.ImportReference(ga.Resolve());
                    }
                }

                MethodReference ftn = module.ImportReference(genExecuteMethod).MakeHostInstanceGeneric(genericArgs.ToArray());
                il.Emit(OpCodes.Ldftn, ftn);
                il.Emit(OpCodes.Newobj, managedJobDelegateCtor);
                il.Emit(OpCodes.Ret);
            }

            method.Body.Optimize();
            return method;
        }

        private MethodDefinition GenGetUnmanagedJobSizeMethodMethod(AssemblyDefinition asm, TypeDefinition type)
        {
            var method = new MethodDefinition(GetUnmanagedJobSizeFn,
                MethodAttributes.Public |
                MethodAttributes.HideBySig |
                MethodAttributes.NewSlot |
                MethodAttributes.Virtual,
                asm.MainModule.ImportReference(typeof(int)));
            var il = method.Body.GetILProcessor();

            // The implementation here will be overriden if bursted
            il.Emit(OpCodes.Ldc_I4, -1);
            il.Emit(OpCodes.Ret);

            return method;
        }

        private MethodDefinition GenGetMarshalMethodMethod(AssemblyDefinition asm, TypeDefinition type)
        {
            var managedJobMarshalDelegate = asm.MainModule.ImportReference(m_JobUtilityDef.NestedTypes.First(i => i.Name == "ManagedJobMarshalDelegate").Resolve());
            var method = new MethodDefinition(GetMarshalMethodFn,
                MethodAttributes.Public |
                MethodAttributes.HideBySig |
                MethodAttributes.NewSlot |
                MethodAttributes.Virtual,
                managedJobMarshalDelegate);
            var il = method.Body.GetILProcessor();

            // The implementation here will be overriden if bursted
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);

            return method;
        }

        MethodDefinition GenScheduleMethod(
            AssemblyDefinition asm,
            TypeDefinition jobTypeDef)
        {
            var method = new MethodDefinition(PrepareJobAtScheduleTimeFn,
                MethodAttributes.Public |
                MethodAttributes.HideBySig |
                MethodAttributes.NewSlot |
                MethodAttributes.Virtual,
                asm.MainModule.ImportReference(typeof(int)));

            // -------- Parameters ---------
            method.Body.InitLocals = true;
            var bc = method.Body.Instructions;

            if (m_AtomicDef != null)
                AddSafetyIL(method, asm, jobTypeDef);

            // Magic number "2" is returned so that we can check (at run time) that code-gen actually occured.
            bc.Add(Instruction.Create(OpCodes.Ldc_I4_2));
            bc.Add(Instruction.Create(OpCodes.Ret));
            method.Body.Optimize();
            return method;
        }

        MethodDefinition FindDeallocate(TypeDefinition td)
        {
            var disposeFnDef = td.Methods.FirstOrDefault(m => m.Name == "Deallocate" && m.Parameters.Count == 0);
            return disposeFnDef;
        }

        MethodDefinition FindDispose(TypeDefinition td)
        {
            var disposeFnDef = td.Methods.FirstOrDefault(m => m.Name == "Dispose" && m.Parameters.Count == 0);
            return disposeFnDef;
        }

        MethodDefinition GenExecuteMethod(
            AssemblyDefinition asm,
            TypeDefinition jobTypeDef)
        {
            var method = new MethodDefinition(PrepareJobAtExecuteTimeFn,
                MethodAttributes.Public |
                MethodAttributes.HideBySig |
                MethodAttributes.NewSlot |
                MethodAttributes.Virtual,
                asm.MainModule.ImportReference(typeof(void)));

            // -------- Parameters ---------
            var paramJobIndex = new ParameterDefinition("jobIndex", ParameterAttributes.None,
                asm.MainModule.ImportReference(typeof(int)));
            method.Parameters.Add(paramJobIndex);

            method.Body.InitLocals = true;
            var bc = method.Body.Instructions;

            AddThreadIndexIL(method, asm, jobTypeDef);

            bc.Add(Instruction.Create(OpCodes.Ret));
            method.Body.Optimize();
            return method;
        }

        static bool FieldHasDeallocOnJobCompletion(FieldDefinition field)
        {
            var deallocateOnJobCompletionAttr = field.CustomAttributes.FirstOrDefault(ca =>
                ca.Constructor.DeclaringType.Name == "DeallocateOnJobCompletionAttribute");
            if (deallocateOnJobCompletionAttr != null)
            {
                var supportsAttribute = field.FieldType.Resolve().CustomAttributes.FirstOrDefault(ca =>
                    ca.Constructor.DeclaringType.Name == "NativeContainerSupportsDeallocateOnJobCompletionAttribute");
                if (supportsAttribute == null)
                    throw new ArgumentException(
                        $"DeallocateOnJobCompletion for {field.FullName} is invalid without NativeContainerSupportsDeallocateOnJobCompletion on {field.FieldType.FullName}");
                return true;
            }

            return false;
        }

        static List<TypeReference> CreateGenericArgs(ModuleDefinition module, FieldReference field)
        {
            GenericInstanceType git = (GenericInstanceType)field.FieldType;
            List<TypeReference> genericArgs = new List<TypeReference>();
            foreach (var specializationType in git.GenericArguments)
            {
                genericArgs.Add(module.ImportReference(specializationType));
            }

            return genericArgs;
        }


        void GenDeallocateIL(
            MethodDefinition method,
            AssemblyDefinition asm,
            TypeDefinition jobTypeDef)
        {
            ILProcessor il = method.Body.GetILProcessor();

            foreach (var field in jobTypeDef.Fields)
            {
                if (FieldHasDeallocOnJobCompletion(field))
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldflda, asm.MainModule.ImportReference(field));

                    var deallocateFnDef = FindDeallocate(field.FieldType.Resolve());
                    deallocateFnDef.IsPublic = true;
                    var deallocateFnRef = asm.MainModule.ImportReference(deallocateFnDef);
                    if (field.FieldType is GenericInstanceType)
                    {
                        List<TypeReference> genericArgs = CreateGenericArgs(asm.MainModule, field);
                        deallocateFnRef =
                            asm.MainModule.ImportReference(deallocateFnDef.MakeHostInstanceGeneric(genericArgs.ToArray()));
                    }

                    if (deallocateFnRef == null)
                        throw new Exception(
                            $"{jobTypeDef.Name}::{field.Name} is missing a {field.FieldType.Name}::Deallocate() implementation");

                    il.Emit(OpCodes.Call, deallocateFnRef);
                }
            }
        }

        void GenWrapperDeallocateIL(
            MethodDefinition method,
            AssemblyDefinition asm,
            TypeDefinition jobTypeDef,
            VariableDefinition jobVar)
        {
            ILProcessor il = method.Body.GetILProcessor();

            // The calling function has done the work of finding the jobVar:
            // CustomJobData<T> jobVar = *(CustomJobData<T>*)ptr;
            // Use that! Don't want to find it again.
            foreach (FieldDefinition field in jobVar.VariableType.Resolve().Fields)
            {
                if (FieldHasDeallocOnJobCompletion(field))
                {
                    il.Emit(OpCodes.Ldloca, jobVar);
                    il.Emit(OpCodes.Ldflda, asm.MainModule.ImportReference(TypeRegGen.MakeGenericFieldSpecialization(field, jobTypeDef.GenericParameters.ToArray())));

                    MethodDefinition deallocateFnDef = FindDispose(field.FieldType.Resolve());
                    deallocateFnDef.IsPublic = true;

                    var deallocateFnRef = asm.MainModule.ImportReference(deallocateFnDef);
                    if (field.FieldType is GenericInstanceType)
                    {
                        List<TypeReference> genericArgs = CreateGenericArgs(asm.MainModule, field);
                        deallocateFnRef =
                            asm.MainModule.ImportReference(deallocateFnDef.MakeHostInstanceGeneric(genericArgs.ToArray()));
                    }

                    if (deallocateFnRef == null)
                        throw new Exception(
                            $"{jobTypeDef.Name}::{field.Name} is missing a {field.FieldType.Name}::Deallocate() implementation");

                    il.Emit(OpCodes.Call, asm.MainModule.ImportReference(deallocateFnRef));
                }
            }
        }

        MethodDefinition GenCleanupMethod(
            AssemblyDefinition asm,
            TypeDefinition jobTypeDef,
            JobDesc jobDesc)
        {
            var method = new MethodDefinition(CleanupJobFn,
                MethodAttributes.Public |
                MethodAttributes.HideBySig |
                MethodAttributes.NewSlot |
                MethodAttributes.Virtual,
                asm.MainModule.ImportReference(typeof(void)));

            method.Parameters.Add(new ParameterDefinition("ptr", ParameterAttributes.None, asm.MainModule.ImportReference(typeof(void*))));

            method.Body.InitLocals = true;
            var bc = method.Body.Instructions;

            GenDeallocateIL(method, asm, jobTypeDef);

            if (m_AtomicDef != null)
                AddCleanupSafetyIL(method, asm, jobTypeDef);

            bc.Add(Instruction.Create(OpCodes.Ret));
            return method;
        }

        static void WalkFieldsRec(TypeDefinition type, List<List<FieldReference>> paths, Func<FieldDefinition, bool> match)
        {
            if (type == null || !type.IsStructValueType())
                return;

            foreach (FieldDefinition f in type.Fields)
            {
                if (f.IsStatic || f.FieldType.IsPointer)
                    continue;

                paths[paths.Count - 1].Add(f);
                var fType = f.FieldType.Resolve();
                if (fType != null)
                {
                    if (match(f))
                    {
                        var endPath = paths[paths.Count - 1];

                        // Duplicate the stack:
                        paths.Add(new List<FieldReference>(endPath));
                    }

                    WalkFieldsRec(fType, paths, match);
                }

                var lastPath = paths[paths.Count - 1];
                lastPath.RemoveAt(lastPath.Count - 1);
            }
        }

        static List<List<FieldReference>> WalkFields(TypeDefinition type, Func<FieldDefinition, bool> match)
        {
            List<List<FieldReference>> paths = new List<List<FieldReference>>();
            paths.Add(new List<FieldReference>());
            WalkFieldsRec(type, paths, match);
            if (paths[paths.Count - 1].Count == 0)
                paths.RemoveAt(paths.Count - 1);
            return paths;
        }

        static FieldReference SpecializeFieldIfPossible(ModuleDefinition module, FieldReference target, TypeReference srcGenerics)
        {
            if (srcGenerics is GenericInstanceType)
            {
                GenericInstanceType git = (GenericInstanceType)srcGenerics;
                List<TypeReference> genericArgs = new List<TypeReference>();
                foreach (TypeReference specializationType in git.GenericArguments)
                {
                    var imp = module.ImportReference(specializationType);
                    genericArgs.Add(imp);
                }

                var closed = TypeRegGen.MakeGenericFieldSpecialization(module.ImportReference(target), genericArgs.ToArray());
                return closed;
            }
            return module.ImportReference(target);
        }

        void AddThreadIndexIL(
            MethodDefinition method,
            AssemblyDefinition asm,
            TypeDefinition jobTypeDef)
        {
            var il = method.Body.GetILProcessor();
            var paramJobIndex = method.Parameters[0];

            IterateFields(jobTypeDef,
                (FieldTypeTuple hierarchy) =>
                {
                    FieldReference field = hierarchy.Last().Item1;
                    TypeReference fieldType = hierarchy.Last().Item2;
                    if (field == null || fieldType == null)
                        return;

                    if (fieldType.MetadataType == MetadataType.Int32)
                    {
                        FieldDefinition td = field.Resolve();
                        td.IsPublic = true;

                        if (td.HasCustomAttributes &&
                            td.CustomAttributes.FirstOrDefault(a =>
                                a.AttributeType.FullName ==
                                "Unity.Collections.LowLevel.Unsafe.NativeSetThreadIndexAttribute") != null)
                        {
                            List<FieldReference> importedHierarchy = new List<FieldReference>();
                            foreach (var set in hierarchy)
                            {
                                importedHierarchy.Add(CreateImportedType(asm.MainModule, set.Item1));
                                set.Item1.Resolve().IsPublic = true;
                            }

                            il.Emit(OpCodes.Ldarg_0);

                            // C# re-orders structures that contain a reference to a class. Since the DisposeSentinel
                            // is in almost every job, every job gets re-ordered. So the normal list returned by GetFieldOffsetsOf
                            // can't account for re-ordering (since we don't even know what the re-order *is*.) Therefore we
                            // need a bunch of ldflda to find them.
                            foreach (var importedRef in importedHierarchy.GetRange(0, importedHierarchy.Count - 1))
                            {
                                il.Emit(OpCodes.Ldflda, importedRef);
                            }

                            il.Emit(OpCodes.Ldarg, paramJobIndex);
                            il.Emit(OpCodes.Stfld, importedHierarchy[importedHierarchy.Count - 1]);
                        }
                    }
                });
        }

        void AddSafetyIL(
            MethodDefinition method,
            AssemblyDefinition asm,
            TypeDefinition jobTypeDef)
        {
            // TODO deal with the generics case.
            // Currently this generates bad IL; much better to not have the safety checks.
            if (!jobTypeDef.HasFields || !jobTypeDef.IsValueType || jobTypeDef.HasGenericParameters)
                return;

            method.Body.InitLocals = true;
            var il = method.Body.GetILProcessor();

            var releaseFnDef = m_AtomicDef.Methods.First(i => i.Name == "Release");
            var patchLocalFnDef = m_AtomicDef.Methods.First(i => i.Name == "PatchLocal");
            var setAllowWriteOnlyFnDef = m_AtomicDef.Methods.First(i => i.Name == "SetAllowWriteOnly");
            var setAllowReadOnlyFnDef = m_AtomicDef.Methods.First(i => i.Name == "SetAllowReadOnly");
            var clearFnDef = m_DisposeSentinelDef.Methods.First(i => i.Name == "Clear");

            IterateFields(jobTypeDef,
                (List<Tuple<FieldReference, TypeReference>> hierarchy) =>
                {
                    var field = hierarchy.Last().Item1;
                    var fieldType = hierarchy.Last().Item2;
                    if (field == null || fieldType == null)
                        return;

                    if (fieldType.IsValueType && !fieldType.IsPrimitive)
                    {
                        TypeDefinition td = fieldType.Resolve();
                        FieldDefinition fd = field.Resolve();

                        bool writeOnly = false;
                        bool readOnly = false;
                        bool needRelease = FieldHasDeallocOnJobCompletion(fd);

                        if (td.HasCustomAttributes &&
                            td.CustomAttributes.FirstOrDefault(a =>
                                a.AttributeType.FullName ==
                                "Unity.Collections.LowLevel.Unsafe.NativeContainerAttribute") != null
                            && td.CustomAttributes.FirstOrDefault(a =>
                                a.AttributeType.FullName ==
                                "Unity.Collections.NativeContainerIsAtomicWriteOnlyAttribute") != null)
                        {
                            writeOnly = true;
                        }

                        if (fd.HasCustomAttributes &&
                            fd.CustomAttributes.FirstOrDefault(a =>
                                a.AttributeType.FullName == "Unity.Collections.WriteOnlyAttribute") != null)
                        {
                            writeOnly = true;
                        }

                        if (fd.HasCustomAttributes && fd.CustomAttributes.FirstOrDefault(a =>
                            a.AttributeType.FullName == "Unity.Collections.ReadOnlyAttribute") != null)
                        {
                            readOnly = true;
                        }

                        if (writeOnly && readOnly)
                        {
                            throw new ArgumentException(
                                $"[ReadOnly] and [WriteOnly] are both specified on '{fd.FullName}'");
                        }

                        var typeResolver = TypeResolver.For(fieldType);

                        // No recursion here - if there are sub-fields which may use atomic safety handles, these
                        // will be treated separately.
                        foreach (var subField in td.Fields)
                        {
                            if (subField.FieldType.FullName == "Unity.Collections.LowLevel.Unsafe.AtomicSafetyHandle")
                            {
                                if (subField.Name != "m_Safety" && subField.Name != "m_Safety0")
                                    continue;
                                
                                subField.IsPublic = true;

                                List<FieldReference> importedHierarchy = new List<FieldReference>();
                                foreach (var set in hierarchy)
                                {
                                    importedHierarchy.Add(CreateImportedType(asm.MainModule, set.Item1));
                                    set.Item1.Resolve().IsPublic = true;
                                }

                                var safetyHandleField = typeResolver.Resolve(subField);
                                FieldReference safetyHandleImported = CreateImportedType(asm.MainModule, safetyHandleField);

                                // AtomicSafetyHandle.Release(result.m_Safety);
                                if (needRelease)
                                {
                                    il.Emit(OpCodes.Ldarg_0);
                                    foreach (var importedRef in importedHierarchy)
                                    {
                                        il.Emit(OpCodes.Ldflda, importedRef);
                                    }
                                    il.Emit(OpCodes.Ldfld, safetyHandleImported);
                                    il.Emit(OpCodes.Call, asm.MainModule.ImportReference(releaseFnDef));
                                }

                                // AtomicSafetyHandle.PatchLocal(ref result.m_Safety);
                                il.Emit(OpCodes.Ldarg_0);
                                foreach (var importedRef in importedHierarchy)
                                {
                                    il.Emit(OpCodes.Ldflda, importedRef);
                                }
                                il.Emit(OpCodes.Ldflda, safetyHandleImported);
                                il.Emit(OpCodes.Call, asm.MainModule.ImportReference(patchLocalFnDef));
                                
                                // AtomicSafetyHandle.SetAllowWriteOnly(ref result.m_Safety);
                                // or
                                // AtomicSafetyHandle.SetAllowReadOnly(ref result.m_Safety);
                                if (writeOnly || readOnly)
                                {
                                    il.Emit(OpCodes.Ldarg_0);
                                    foreach (var importedRef in importedHierarchy)
                                    {
                                        il.Emit(OpCodes.Ldflda, importedRef);
                                    }
                                    il.Emit(OpCodes.Ldflda, safetyHandleImported);

                                    if (writeOnly)
                                        il.Emit(OpCodes.Call, asm.MainModule.ImportReference(setAllowWriteOnlyFnDef));
                                    if (readOnly)
                                        il.Emit(OpCodes.Call, asm.MainModule.ImportReference(setAllowReadOnlyFnDef));
                                }
                            }
                        }

                        if (needRelease)
                        {
                            foreach (var subField in td.Fields)
                            {
                                if (subField.FieldType.FullName == "Unity.Collections.LowLevel.Unsafe.DisposeSentinel")
                                {
                                    subField.IsPublic = true;

                                    List<FieldReference> importedHierarchy = new List<FieldReference>();
                                    foreach (var set in hierarchy)
                                    {
                                        importedHierarchy.Add(CreateImportedType(asm.MainModule, set.Item1));
                                    }

                                    var disposeField = typeResolver.Resolve(subField);
                                    FieldReference disposeImported = CreateImportedType(asm.MainModule, disposeField);

                                    il.Emit(OpCodes.Ldarg_0);
                                    foreach (var importedRef in importedHierarchy)
                                    {
                                        il.Emit(OpCodes.Ldflda, importedRef);
                                    }
                                    //il.Emit(OpCodes.Ldflda, asm.MainModule.ImportReference(fd));
                                    il.Emit(OpCodes.Ldflda, disposeImported);
                                    il.Emit(OpCodes.Call, asm.MainModule.ImportReference(clearFnDef));
                                }
                            }
                        }
                    }
                });
        }

        void AddCleanupSafetyIL(
            MethodDefinition method,
            AssemblyDefinition asm,
            TypeDefinition jobTypeDef)
        {
            // TODO deal with the generics case.
            // Currently this generates bad IL; much better to not have the safety checks.
            if (!jobTypeDef.HasFields || !jobTypeDef.IsValueType || jobTypeDef.HasGenericParameters)
                return;

            method.Body.InitLocals = true;
            var il = method.Body.GetILProcessor();

            var unpatchLocalFnDef = m_AtomicDef.Methods.First(i => i.Name == "UnpatchLocal");

            IterateFields(jobTypeDef,
                (List<Tuple<FieldReference, TypeReference>> hierarchy) =>
                {
                    var field = hierarchy.Last().Item1;
                    var fieldType = hierarchy.Last().Item2;
                    if (field == null || fieldType == null)
                        return;

                    if (fieldType.IsValueType && !fieldType.IsPrimitive)
                    {
                        TypeDefinition td = fieldType.Resolve();
                        FieldDefinition fd = field.Resolve();

                        var typeResolver = TypeResolver.For(fieldType);

                        // No recursion here - if there are sub-fields which may use atomic safety handles, these
                        // will be treated separately.
                        foreach (var subField in td.Fields)
                        {
                            if (subField.FieldType.FullName == "Unity.Collections.LowLevel.Unsafe.AtomicSafetyHandle")
                            {
                                if (subField.Name != "m_Safety" && subField.Name != "m_Safety0")
                                    continue;

                                List<FieldReference> importedHierarchy = new List<FieldReference>();
                                foreach (var set in hierarchy)
                                {
                                    importedHierarchy.Add(CreateImportedType(asm.MainModule, set.Item1));
                                }

                                var safetyHandleField = typeResolver.Resolve(subField);
                                FieldReference safetyHandleImported = CreateImportedType(asm.MainModule, safetyHandleField);

                                // AtomicSafetyHandle.UnpatchLocal(ref result.m_Safety);
                                il.Emit(OpCodes.Ldarg_0);
                                foreach (var importedRef in importedHierarchy)
                                {
                                    il.Emit(OpCodes.Ldflda, importedRef);
                                }
                                il.Emit(OpCodes.Ldflda, safetyHandleImported);
                                il.Emit(OpCodes.Call, asm.MainModule.ImportReference(unpatchLocalFnDef));
                            }
                        }
                    }
                });
        }
    }
}
