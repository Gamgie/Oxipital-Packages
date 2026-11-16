using Oxipital;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxipital
{
    public class BaseManager<T> : BaseItem where T : BaseItem
    {
        [Range(0,16)]
        public float count = 1;

        public GameObject itemPrefab;
        internal List<T> items;

        string itemName = "Item";

        public BaseManager(string itemName = "Item")
        {
            this.itemName = itemName;
        }

        virtual protected void OnEnable()
        {
            items = GetComponentsInChildren<T>().ToList();
        }

        override protected void Update()
        {
            base.Update();
            float target = activeCount;
            while (Mathf.Ceil(target) < items.Count) removeLastItem();
            while (Mathf.Ceil(target) > items.Count) addItem();
        }

        /// <summary>The count actually used: count, unless a subclass takes it from elsewhere (a slave dancer group takes its
        /// master's).</summary>
        protected virtual float activeCount => count;

        virtual protected T addItem()
        {
            GameObject item = Instantiate(itemPrefab, transform);
            item.gameObject.name = itemName+" "+ (items.Count + 1);
            T result = item.GetComponent<T>();
            items.Add(result);
            return result;

        }

        void removeLastItem()
        {
            if (items[items.Count - 1] != null) killLastItem();
            items.RemoveAt(items.Count - 1);
        }

        virtual protected void killLastItem() { 
            items[items.Count -1].kill(getKillTime()); 
        }


        //Virtual to override by child classes
        protected virtual float getKillTime() { return .1f; }

        public virtual float getCount()
    	{
            return count;
    	}
    }
}
