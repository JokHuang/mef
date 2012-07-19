﻿// -----------------------------------------------------------------------
// Copyright © Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------

using System.Reflection;
using System.Composition.Hosting.Util;
using System.Composition.Hosting.Core;
using System.Composition.Runtime;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using System.Composition.Hosting.Providers.Metadata;

namespace System.Composition.Hosting.Providers.ExportFactory
{
    using System.Composition.Hosting.Properties;

    class ExportFactoryWithMetadataExportDescriptorProvider : ExportDescriptorProvider
    {
        static readonly MethodInfo GetLazyDefinitionsMethod =
            typeof(ExportFactoryWithMetadataExportDescriptorProvider).GetTypeInfo().GetDeclaredMethod("GetExportFactoryDescriptors");

        public override IEnumerable<ExportDescriptorPromise> GetExportDescriptors(CompositionContract contract, DependencyAccessor definitionAccessor)
        {
            if (!contract.ContractType.GetTypeInfo().IsGenericType ||
                        contract.ContractType.GetGenericTypeDefinition() != typeof(ExportFactory<,>))
                return NoExportDescriptors;

            var ga = contract.ContractType.GenericTypeArguments;
            var gld = GetLazyDefinitionsMethod.MakeGenericMethod(ga[0], ga[1]);
            var gldm = gld.CreateStaticDelegate<Func<CompositionContract, DependencyAccessor, object>>();
            return (ExportDescriptorPromise[])gldm(contract, definitionAccessor);
        }

        static ExportDescriptorPromise[] GetExportFactoryDescriptors<TProduct, TMetadata>(CompositionContract exportFactoryContract, DependencyAccessor definitionAccessor)
        {
            var productContract = exportFactoryContract.ChangeType(typeof(TProduct));
            var boundaries = new string[0];

            IEnumerable<string> specifiedBoundaries;
            CompositionContract unwrapped;
            if (exportFactoryContract.TryUnwrapMetadataConstraint(Constants.SharingBoundaryImportMetadataConstraintName, out specifiedBoundaries, out unwrapped))
            {
                productContract = unwrapped.ChangeType(typeof(TProduct));
                boundaries = (specifiedBoundaries ?? new string[0]).ToArray();
            }

            var metadataProvider = MetadataViewProvider.GetMetadataViewProvider<TMetadata>();

            return definitionAccessor.ResolveDependencies("product", productContract, false)
                .Select(d => new ExportDescriptorPromise(
                    exportFactoryContract,
                    typeof(ExportFactory<TProduct, TMetadata>).Name,
                    false,
                    () => new[] { d },
                    _ =>
                    {
                        var dsc = d.Target.GetDescriptor();
                        return ExportDescriptor.Create((c, o) =>
                        {
                            return new ExportFactory<TProduct, TMetadata>(() =>
                            {
                                var lifetimeContext = new LifetimeContext(c, boundaries);
                                return Tuple.Create<TProduct, Action>((TProduct)CompositionOperation.Run(lifetimeContext, dsc.Activator), lifetimeContext.Dispose);
                            },
                            metadataProvider(dsc.Metadata));
                        },
                        dsc.Metadata);
                    }))
                .ToArray();
        }
    }
}