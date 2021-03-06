declare namespace GrandTheftMultiplayer.Client.Contracts {

	interface IStreamedItem {
		RemoteHandle: number;
		LocalOnly: boolean;
		StreamedIn: boolean;
		readonly Position: GrandTheftMultiplayer.Shared.Math.Vector3;
		EntityType: number;
		Dimension: number;
		AttachedTo: GrandTheftMultiplayer.Shared.Attachment;
		Attachables: System.Collections.Generic.List<number>;
		PositionMovement: GrandTheftMultiplayer.Shared.Movement;
		RotationMovement: GrandTheftMultiplayer.Shared.Movement;
		readonly MaximumStreamDistance: number;
		readonly MaximumAmountStreamed: number;
	}

}
