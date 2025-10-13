#region License
// Copyright (c) 2026 Agenix
//
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License") -
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#endregion

using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Agenix.PlaywrightGrid.Shared.Extensibility.Commands.CommandArgs;

namespace Agenix.PlaywrightGrid.Shared.Execution.Metadata;

internal class MetaAttributesCollection : IMetaAttributesCollection
{
    private readonly ObservableCollection<MetaAttribute> _attributes;
    private readonly TestCommandsSource _commandsSource;
    private readonly ITestContext _testContext;

    public MetaAttributesCollection(ITestContext testContext, TestCommandsSource commandsSource)
    {
        _testContext = testContext;
        _commandsSource = commandsSource;

        var commandArgs = new TestAttributesCommandArgs(null);

        TestCommandsSource.RaiseOnGetTestAttributes(_commandsSource, _testContext, commandArgs);

        _attributes = new ObservableCollection<MetaAttribute>(commandArgs.Attributes);

        _attributes.CollectionChanged += _attributes_CollectionChanged;
    }

    public int Count => _attributes.Count;

    public bool IsReadOnly => false;

    public void Add(MetaAttribute item)
    {
        _attributes.Add(item);
    }

    public void Add(string key, string value)
    {
        var item = new MetaAttribute(key, value);

        Add(item);
    }

    public void Clear()
    {
        _attributes.Clear();
    }

    public bool Contains(MetaAttribute item)
    {
        return _attributes.Contains(item);
    }

    public void CopyTo(MetaAttribute[] array, int arrayIndex)
    {
        _attributes.CopyTo(array, arrayIndex);
    }

    public IEnumerator<MetaAttribute> GetEnumerator()
    {
        return _attributes.GetEnumerator();
    }

    public bool Remove(MetaAttribute item)
    {
        return _attributes.Remove(item);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _attributes.GetEnumerator();
    }

    private void _attributes_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            var attributes = new Collection<MetaAttribute>();
            foreach (MetaAttribute attribute in e.NewItems)
            {
                attributes.Add(attribute);
            }

            var args = new TestAttributesCommandArgs(attributes);

            TestCommandsSource.RaiseOnAddTestAttributes(_commandsSource, _testContext, args);
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove)
        {
            var attributes = new Collection<MetaAttribute>();
            foreach (MetaAttribute attribute in e.OldItems)
            {
                attributes.Add(attribute);
            }

            var args = new TestAttributesCommandArgs(attributes);

            TestCommandsSource.RaiseOnRemoveTestAttributes(_commandsSource, _testContext, args);
        }
    }
}
