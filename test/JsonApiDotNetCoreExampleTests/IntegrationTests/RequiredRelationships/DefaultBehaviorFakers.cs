using System;
using Bogus;
using TestBuildingBlocks;

namespace JsonApiDotNetCoreExampleTests.IntegrationTests.RequiredRelationships
{
    internal sealed class DefaultBehaviorFakers : FakerContainer
    {
        private readonly Lazy<Faker<Order>> _orderFaker = new Lazy<Faker<Order>>(() =>
            new Faker<Order>()
                .UseSeed(GetFakerSeed())
                .RuleFor(order => order.Amount, f => f.Finance.Amount())
        );

        private readonly Lazy<Faker<Customer>> _customerFaker = new Lazy<Faker<Customer>>(() =>
            new Faker<Customer>()
                .UseSeed(GetFakerSeed())
                .RuleFor(customer => customer.EmailAddress, f => f.Person.Email)
        );

        private readonly Lazy<Faker<Shipment>> _shipmentFaker = new Lazy<Faker<Shipment>>(() =>
            new Faker<Shipment>()
                .UseSeed(GetFakerSeed())
                .RuleFor(shipment => shipment.TrackAndTraceCode, f => f.Commerce.Ean13())
                .RuleFor(shipment => shipment.ShippedAt, f => f.Date.Past())
        );

        public Faker<Order> Orders => _orderFaker.Value;
        public Faker<Customer> Customers => _customerFaker.Value;
        public Faker<Shipment> Shipments => _shipmentFaker.Value;
    }
}
