using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleCore
{
    internal class EnumrableTest<T> : IAsyncEnumerable<T>, IEnumerable<T>
    {
        private List<T> list { get; set; }
        public EnumrableTest(List<T> source)
        {
            list = source;
        }
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new EnumratorTest<T>(list);
        }

        public IEnumerator<T> GetEnumerator()
        { 
            return new EnumratorTest<T>(list);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
    internal class EnumratorTest<T> : IAsyncEnumerator<T>, IEnumerator<T>
    {
        private readonly IList<T> _asyncSource;

        int _position = -1;
        public EnumratorTest(IList<T> asyncSource)
        {
            _asyncSource = asyncSource;
        }

        public T Current => _position == -1 ? default : _asyncSource[_position];

        object IEnumerator.Current => Current;

        public void Dispose()
        {

        }

        public async ValueTask DisposeAsync()
        {
            Dispose();
        }

        public bool MoveNext()
        {
            if (_position >= _asyncSource.Count - 1)
            {
                return false;
            }
            _position++;
            return true;
        }

        public async ValueTask<bool> MoveNextAsync()
        {
            return await Task.FromResult(MoveNext());
        }

        public void Reset()
        {
            _position = -1;
        }
    }
}
