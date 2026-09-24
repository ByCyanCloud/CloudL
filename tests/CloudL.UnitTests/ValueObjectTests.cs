using CloudL.Domain.ValueObjects;

namespace CloudL.UnitTests;

/// <summary>
/// 值对象相等性测试。
/// </summary>
public class ValueObjectTests
{
    [Fact]
    public void ValueObject_ShouldUseStructuralEquality()
    {
        var address1 = new SampleAddress("Street 1", "City A");
        var address2 = new SampleAddress("Street 1", "City A");
        var address3 = new SampleAddress("Street 2", "City B");

        Assert.Equal(address1, address2);
        Assert.NotEqual(address1, address3);
        Assert.True(address1 == address2);
        Assert.False(address1 == address3);
    }

    [Fact]
    public void ValueObject_ShouldProduceSameHashCode_ForEqualInstances()
    {
        var address1 = new SampleAddress("Street 1", "City A");
        var address2 = new SampleAddress("Street 1", "City A");

        Assert.Equal(address1.GetHashCode(), address2.GetHashCode());
    }

    [Fact]
    public void ValueObject_ShouldNotEqualNull()
    {
        var address = new SampleAddress("Street 1", "City A");

        Assert.False(address.Equals(null));
        Assert.True(address != null);
    }

    private sealed class SampleAddress : ValueObject
    {
        public SampleAddress(string street, string city)
        {
            Street = street;
            City = city;
        }

        public string Street { get; }

        public string City { get; }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Street;
            yield return City;
        }
    }
}
