﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using KenticoCloud.Delivery;
using Microsoft.Extensions.Caching.Memory;
using System.Threading;
using WebhookCacheInvalidationMvc.Helpers;
using WebhookCacheInvalidationMvc.Models;
using Microsoft.Extensions.Primitives;

namespace WebhookCacheInvalidationMvc.Services
{
    public class CacheManager : ICacheManager, IDisposable
    {
        private bool _disposed = false;
        private readonly IMemoryCache _memoryCache;
        //private readonly IDeliveryClient _deliveryClient;

        public int CacheExpirySeconds
        {
            get;
            set;
        }

        public List<string> CacheKeys
        {
            get;
            set;
        }

        public CacheManager(IMemoryCache memoryCache) //, IDeliveryClient deliveryClient)
        {
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            //_deliveryClient = deliveryClient ?? throw new ArgumentNullException(nameof(deliveryClient));

            // HACK
            CacheExpirySeconds = 60;
        }

        public async Task<T> GetOrCreateAsync<T>(Func<Task<T>> contentFactory, Func<T, DependencyGroup> dependencyGroupFactory, IEnumerable<string> identifierTokens)
        {
            if (!_memoryCache.TryGetValue(StringHelpers.Join(identifierTokens), out T cacheEntry))
            {
                T response = await contentFactory.Invoke();
                await Task.Run(() => CreateEntry(response, dependencyGroupFactory, identifierTokens));

                return response;
            }

            return cacheEntry;
        }

        /// <summary>
        /// The <see cref="IDisposable.Dispose"/> implementation.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected void CreateEntry<T>(T response, Func<T, DependencyGroup> dependencyGroupFactory, IEnumerable<string> identifierTokens)
        {
            var dependencyGroupList = _memoryCache.GetOrCreate(CacheHelper.DEPENDENCY_GROUP_LIST_ENTRY_KEY, entry => { return new List<DependencyGroup>(); });
            DependencyGroup currentDependencyGroup = dependencyGroupFactory.Invoke(response);
            CancellationTokenSource currentCancellationTokenSource = null;

            if (!dependencyGroupList.Contains(currentDependencyGroup, new DependencyGroupEqualityComparer()))
            {
                dependencyGroupList.Add(currentDependencyGroup);
                _memoryCache.Set(CacheHelper.DEPENDENCY_GROUP_LIST_ENTRY_KEY, dependencyGroupList, new MemoryCacheEntryOptions { Priority = CacheItemPriority.NeverRemove });
                currentCancellationTokenSource = currentDependencyGroup.CancellationTokenSource;
            }
            else
            {
                currentCancellationTokenSource = dependencyGroupList.First(dg => new DependencyGroupEqualityComparer().Equals(dg, currentDependencyGroup)).CancellationTokenSource;
            }

            var cacheEntryOptions = new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromSeconds(CacheExpirySeconds)).AddExpirationToken(new CancellationChangeToken(currentCancellationTokenSource.Token));
            _memoryCache.Set(StringHelpers.Join(identifierTokens), response, cacheEntryOptions);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _memoryCache.Dispose();
            }

            _disposed = true;
        }
    }
}
