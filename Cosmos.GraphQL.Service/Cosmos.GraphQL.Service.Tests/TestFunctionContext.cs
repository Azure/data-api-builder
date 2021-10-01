using System;
using System.Collections.Generic;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;

namespace Cosmos.GraphQL.Service.Tests
{
    /// <summary>
    /// This is a test implementation for FunctionContext.
    /// FunctionContext is required to call Azure Function entry point.
    /// </summary>
    public class TestFunctionContext : FunctionContext
    {
        public override string InvocationId => throw new NotImplementedException();

        public override string FunctionId => throw new NotImplementedException();

        public override TraceContext TraceContext => throw new NotImplementedException();

        public override BindingContext BindingContext => throw new NotImplementedException();

        public override IServiceProvider InstanceServices { get; set; }

        public override FunctionDefinition FunctionDefinition => throw new NotImplementedException();

        public override IDictionary<object, object> Items { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override IInvocationFeatures Features => throw new NotImplementedException();

        public static TestFunctionContext Create()
        {
            TestFunctionContext functionContext = new TestFunctionContext();

            ServiceCollection services = new ServiceCollection();
            services.AddOptions();
            services.AddFunctionsWorkerDefaults();

            functionContext.InstanceServices = services.BuildServiceProvider();

            return functionContext;
        }

    }
}
