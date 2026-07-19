using System.Collections;

namespace PolymarketLab.SharedKernel.Errors;

public sealed partial class Error
{
    public class ErrorList : IEnumerable<Error>
    {
        private readonly List<Error> _list;

        public ErrorList(List<Error> list)
        {
            _list = list;
        }

        public IEnumerator<Error> GetEnumerator()
        {
            return _list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        //���������� ��������� ����������
        //���� ����������� ��������� �� ���������� ������ ������
        public static implicit operator ErrorList(List<Error> errors)
        {
            return new ErrorList(errors);
        }

        //���� ����������� ���� ������ �� ���������� ������ � ����� �������
        public static implicit operator ErrorList(Error error)
        {
            return new ErrorList(new List<Error>([error]));
        }
    }
}
