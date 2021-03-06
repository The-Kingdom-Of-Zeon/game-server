using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using GrandTheftMultiplayer.Server.API;
using GrandTheftMultiplayer.Server.Elements;
using GrandTheftMultiplayer.Shared;
using GrandTheftMultiplayer.Shared.Math;
using GTA_RP.Houses;
using GTA_RP.Misc;
using GTA_RP.Map;

namespace GTA_RP
{
    /// <summary>
    /// Struct for building entrance
    /// </summary>
    struct HouseEntrance
    {
        public int id;
        public int building_id;
        public Vector3 coordinates;
        public String name;
    }

    /// <summary>
    /// Struct for building exit
    /// </summary>
    struct HouseExit
    {
        public int id;
        public int house_template_id;
        public Vector3 coordinates;
    }

    /// <summary>
    /// Struct to link entrance and exit
    /// </summary>
    struct HouseEntranceExit
    {
        public int entrance_id;
        public int exit_id;
    }

    /// <summary>
    /// Class responsible for handling everything related to buildings like entering and exiting
    /// </summary>
    class HouseManager : Singleton<HouseManager>
    {
        private List<HouseTemplate> houseTemplates = new List<HouseTemplate>();
        private List<Teleport> houseTeleports = new List<Teleport>();
        private List<Teleport> houseExitTeleports = new List<Teleport>();
        private List<House> ownedHouses = new List<House>();
        private Dictionary<int, int> buildingIdForTemplateId = new Dictionary<int, int>();
        private Dictionary<int, String> buildingNames = new Dictionary<int, string>();
        private Dictionary<int, Timer> enterHouseTimers = new Dictionary<int, Timer>();
        private HouseMarket houseMarket;
        private int entryId = 0;

        private const int houseEnterTime = 2000;

        /// <summary>
        /// Constructor of HouseManager
        /// </summary>
        public HouseManager() { }

        /// <summary>
        /// Initializes the housing market.
        /// </summary>
        private void InitHouseMarket()
        {
            houseMarket = new HouseMarket();
        }

        /// <summary>
        /// Initialize all event subscriptions.
        /// </summary>
        private void InitEvents()
        {
            PlayerManager.Instance().SubscribeToPlayerDisconnectEvent(this.OnPlayerDisconnect);
        }


        /// <summary>
        /// Initializes names of the buildings from database
        /// Is ran on script startup
        /// </summary>
        private void InitBuildings()
        {
            DBManager.SelectQuery("SELECT * FROM buildings", (MySql.Data.MySqlClient.MySqlDataReader reader) =>
            {
                this.buildingNames.Add(reader.GetInt32(0), reader.GetString(1));
                if (reader.GetInt32(2) == 1)
                { // Use blip = true 
                    MapManager.Instance().AddBlipToMap(reader.GetInt32(3), reader.GetString(1), reader.GetFloat(4), reader.GetFloat(5), reader.GetFloat(6));
                }
            }).Execute();
        }

        private void AddHouse(int id, int ownerId, int templateId, string name)
        {
            if (id > this.entryId)
            {
                this.entryId = id;
            }
            House h = new House(id, ownerId, templateId, name);
            ownedHouses.Add(h);
        }

        /// <summary>
        /// Loads all buildings from the database and initializes them
        /// Is ran on script startup
        /// </summary>
        private void LoadHousesFromDB()
        {
            DBManager.SelectQuery("SELECT * FROM houses", (MySql.Data.MySqlClient.MySqlDataReader reader) =>
            {
                this.AddHouse(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3));
            }).Execute();
        }

        /// <summary>
        /// Gets all the entrances for buildings
        /// </summary>
        /// <returns>List of all building entrances</returns>
        private List<HouseEntrance> GetHouseEntrances()
        {
            List<HouseEntrance> entrances = new List<HouseEntrance>();
            DBManager.SelectQuery("SELECT * FROM house_teleports", (MySql.Data.MySqlClient.MySqlDataReader reader) =>
            {
                HouseEntrance entrance;
                entrance.id = reader.GetInt32(0);
                entrance.building_id = reader.GetInt32(1);
                entrance.coordinates = new Vector3(reader.GetFloat(2), reader.GetFloat(3), reader.GetFloat(4));
                entrance.name = reader.GetString(5);
                entrances.Add(entrance);
            }).Execute();
            return entrances;
        }

        // Check if character is in any house and if it is, then remove it upon disconnect
        private void OnPlayerDisconnect(Character character)
        {
            foreach (House house in this.ownedHouses)
            {
                if (house.HasOccupant(character))
                {
                    house.RemoveOccupant(character);
                    break;
                }
            }
        }

        /// <summary>
        /// Gets all the exits for buildings
        /// </summary>
        /// <returns>List of all building exits</returns>
        private List<HouseExit> GetHouseExits()
        {
            List<HouseExit> exits = new List<HouseExit>();
            DBManager.SelectQuery("SELECT * FROM house_exits", (MySql.Data.MySqlClient.MySqlDataReader reader) =>
            {
                HouseExit exit;
                exit.id = reader.GetInt32(0);
                exit.house_template_id = reader.GetInt32(1);
                exit.coordinates = new Vector3(reader.GetFloat(2), reader.GetFloat(3), reader.GetFloat(4));
                exits.Add(exit);
            }).Execute();
            return exits;
        }

        /// <summary>
        /// Gets a building entrance with certain id
        /// </summary>
        /// <param name="id">Id of the building entrance</param>
        /// <param name="entrances">List of building entrances to choose from</param>
        /// <returns>Building entrance of the id</returns>
        private HouseEntrance GetEntranceWithId(int id, List<HouseEntrance> entrances)
        {
            return entrances.Single(e => e.id == id);
        }

        /// <summary>
        /// Gets a building exit with certain id
        /// </summary>
        /// <param name="id">Id of the building exit</param>
        /// <param name="exits">List of building exits to choose from</param>
        /// <returns>Building exit of the id</returns>
        private HouseExit GetExitWithId(int id, List<HouseExit> exits)
        {
            return exits.Single(e => e.id == id);
        }

        /// <summary>
        /// Gets a building id for the selected template of house
        /// </summary>
        /// <param name="templateId">Id of the house template</param>
        /// <returns>Building id</returns>
        private int GetBuildingIdForTemplateId(int templateId)
        {
            return this.buildingIdForTemplateId.Get(templateId);
        }

        /// <summary>
        /// Gets apartments in building that are owned by certain character
        /// </summary>
        /// <param name="buildingId">The building to search apartments in</param>
        /// <param name="character">Character whose apartments are searched</param>
        /// <returns>List of owned apartments by the character in specified building</returns>
        private List<House> GetOwnedApartmentsInBuilding(int buildingId, Character character)
        {
            List<House> tempHouses = ownedHouses.Where(h => h.ownerId == character.ID && buildingId == GetBuildingIdForTemplateId(h.templateId)).ToList();
            return tempHouses;
        }

        /// <summary>
        /// Gets apartment names from a list of apartments
        /// </summary>
        /// <param name="apartments">List of apartments</param>
        /// <returns>Names of all apartments in the list</returns>
        private List<string> GetApartmentNames(List<House> apartments)
        {
            return apartments.Select(h => h.name).ToList();
        }

        /// <summary>
        /// Gets apartment ids from a list of apartments
        /// </summary>
        /// <param name="apartments">List of apartments</param>
        /// <returns>Ids of all apartments in the list</returns>
        private List<int> GetApartmentIds(List<House> apartments)
        {
            return apartments.Select(h => h.id).ToList();
        }

        /// <summary>
        /// Get places in building where character is invited to
        /// </summary>
        /// <param name="character">Character whose invitations are checked</param>
        /// <param name="buildingId">Building in which character's invitations are checked</param>
        /// <returns>Places in selected building where character is invited</returns>
        private List<House> GetInvitedPlacesInBuilding(Character character, int buildingId)
        {
            return ownedHouses.Where(h => h.IsInvited(character)).ToList();
        }

        /// <summary>
        /// Gets all places in building where character is invited into
        /// </summary>
        /// <param name="character">Character to check for allowed places</param>
        /// <param name="buildingId">Building for which to check</param>
        /// <returns>List of places where selected character is invited into</returns>
        private List<House> GetPlacesAllowedToEnterInBuilding(Character character, int buildingId)
        {
            List<House> places = new List<House>();
            places.AddRange(GetOwnedApartmentsInBuilding(buildingId, character));
            places.AddRange(GetInvitedPlacesInBuilding(character, buildingId));
            return places;
        }

        /// <summary>
        /// Gets all house entrance-exit pairs
        /// </summary>
        /// <returns>All house entrance-exit pairs</returns>
        private List<HouseEntranceExit> GetHouseEntranceExitPairs()
        {
            List<HouseEntranceExit> exits = new List<HouseEntranceExit>();
            DBManager.SelectQuery("SELECT * FROM house_teleport_links", (MySql.Data.MySqlClient.MySqlDataReader reader) =>
            {
                HouseEntranceExit entranceExit;
                entranceExit.entrance_id = reader.GetInt32(0);
                entranceExit.exit_id = reader.GetInt32(1);
                exits.Add(entranceExit);
            }).Execute();
            return exits;
        }

        /// <summary>
        /// Creates house entry-exit teleport pairs
        /// Is ran at server startup
        /// </summary>
        private void CreateHouseExitTeleports()
        {
            List<HouseExit> exits = GetHouseExits();
            List<HouseEntrance> entrances = GetHouseEntrances();
            List<HouseEntranceExit> entranceExits = GetHouseEntranceExitPairs();

            foreach (HouseExit exit in exits)
            {

                List<TeleportDestination> entrancesT = new List<TeleportDestination>();
                foreach (HouseEntranceExit pair in entranceExits)
                {
                    if (pair.exit_id == exit.id)
                    {
                        HouseEntrance entrance = GetEntranceWithId(pair.entrance_id, entrances);

                        TeleportDestination td;
                        td.id = pair.entrance_id;
                        td.location = entrance.coordinates;
                        td.name = entrance.name;
                        entrancesT.Add(td);
                    }
                }

                Teleport t = new Teleport(exit.house_template_id, exit.coordinates, 0, this.EnterHouseExitTeleport, entrancesT);
                houseExitTeleports.Add(t);
            }

        }

        /// <summary>
        /// Creates the house teleports based on previously loaded information from database
        /// </summary>
        private void CreateHouseTeleports()
        {
            List<HouseEntrance> entrances = GetHouseEntrances();
            List<HouseExit> exits = GetHouseExits();
            List<HouseEntranceExit> entranceExits = GetHouseEntranceExitPairs();

            foreach (HouseEntrance entrance in entrances)
            {
                List<TeleportDestination> exitsT = new List<TeleportDestination>();
                foreach (HouseEntranceExit pair in entranceExits)
                {
                    if (pair.entrance_id == entrance.id)
                    {
                        HouseExit exit = GetExitWithId(pair.exit_id, exits);
                        TeleportDestination td;
                        td.id = exit.house_template_id;
                        td.location = exit.coordinates;
                        td.name = "";
                        exitsT.Add(td);
                    }
                }

                Teleport teleport = new Teleport(entrance.building_id, entrance.coordinates, 0, this.EnterHouseTeleport, exitsT);
                houseTeleports.Add(teleport);
            }
        }

        /// <summary>
        /// Gets a place with certain id
        /// </summary>
        /// <param name="houseId">Id for which to search place for</param>
        /// <returns>A house with selected id</returns>
        private House GetHouseForId(int houseId)
        {
            return ownedHouses.SingleOrDefault(h => h.id == houseId);
        }

        /// <summary>
        /// Gets exit teleport for character in certain range
        /// </summary>
        /// <param name="character">Character for which the teleport is searched for</param>
        /// <param name="teleportId">Id of teleport to look for</param>
        /// <returns>A teleport in certain distance of player and with certain id</returns>
        private Teleport GetExitTeleportForIdAndInRange(Character character, int teleportId)
        {
            return houseExitTeleports.SingleOrDefault(t => t.id == teleportId && t.IsCharacterInsideTeleport(character));
        }

        /// <summary>
        /// Sets a timer for character in which the enter/exit house menu is not displayed until the timer runs out
        /// </summary>
        /// <param name="character">Character for which to set the timer</param>
        private void SetTimerForCharacter(Character character)
        {
            if (enterHouseTimers.Get(character.ID) == null)
            {
                Timer timer = new Timer(houseEnterTime);
                timer.Elapsed += StopTimer;
                timer.AutoReset = false;
                enterHouseTimers.Add(character.ID, timer);
                timer.Enabled = true;
            }
            else
            {
                Timer timer = enterHouseTimers.Get(character.ID);
                timer.Enabled = true;
            }
        }

        /// <summary>
        /// Gets the place where character is inside
        /// </summary>
        /// <param name="character">Character</param>
        /// <returns>A place with selected character as occupant</returns>
        private House GetHouseWithCharacterAsOccupant(Character character)
        {
            return ownedHouses.Single(h => h.HasOccupant(character));
        }

        /// <summary>
        /// Checks if house exit/entry timer is active for character
        /// </summary>
        /// <param name="character">Character</param>
        /// <returns>True if the exit/entry timer is active for character, otherwise false</returns>
        private Boolean IsTimerActiveForPlayer(Character character)
        {
            Timer timer = enterHouseTimers.Get(character.ID);
            if (timer != null)
            {
                return timer.Enabled;
            }

            return false;
        }

        /// <summary>
        /// Gets template id for a house id
        /// </summary>
        /// <param name="houseId">House id</param>
        /// <returns>Template id</returns>
        private int GetTemplateIdForId(int houseId)
        {
            return GetHouseForId(houseId).templateId;
        }

        /// <summary>
        /// Stops the exit/entry timer
        /// </summary>
        /// <param name="source">Timer</param>
        /// <param name="args">Timer arguments</param>
        private void StopTimer(System.Object source, ElapsedEventArgs args)
        {
            Timer timer = (Timer)source;
            timer.Enabled = false;
        }

        /// <summary>
        /// Makes character to enter a house
        /// </summary>
        /// <param name="character">Character who enters the house</param>
        /// <param name="house">House to which enter</param>
        /// <param name="teleport">Teleport used to enter the house</param>
        private void EnterHouse(Character character, House house, Teleport teleport)
        {
            SetTimerForCharacter(character);
            teleport.UseTeleport(character, house.templateId);
            character.owner.client.dimension = house.id;
            house.AddOccupant(character);
        }

        /// <summary>
        /// Makes character to exit a house
        /// </summary>
        /// <param name="character">Character who exits the house</param>
        /// <param name="teleport">House that the character will exit</param>
        /// <param name="destinationId">Entrance id</param>
        private void ExitHouse(Character character, Teleport teleport, int destinationId)
        {
            SetTimerForCharacter(character);
            teleport.UseTeleport(character, destinationId);
            character.owner.client.dimension = 0;
            House house = GetHouseWithCharacterAsOccupant(character);
            if (house != null)
            {
                house.RemoveOccupant(character);
            }
        }

        /// <summary>
        /// Checks if character is allowed to enter a place
        /// </summary>
        /// <param name="character">Character to check</param>
        /// <param name="house">House to check whether character is allowed to enter</param>
        /// <returns>True if character is allowed to enter the house, otherwise false</returns>
        private Boolean IsAllowedToEnterPlace(Character character, House house)
        {
            if (house.ownerId == character.ID || house.IsInvited(character))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Returns all the places that the character owns
        /// </summary>
        /// <param name="character">Character whose places are returned</param>
        /// <returns>All owned places for character</returns>
        private List<House> GetOwnedHouses(Character character)
        {
            return ownedHouses.Where(x => x.ownerId == character.ID).ToList();
        }

        // Public functions

        /// <summary>
        /// Requests an entry to a place for certain player
        /// </summary>
        /// <param name="player">Sender of the request</param>
        /// <param name="houseId">Id of the place</param>
        public void RequestEnterHouse(Character character, int houseId)
        {
            House house = GetHouseForId(houseId);
            Teleport teleport = this.GetInRangeTeleportForHouseTemplate(character, house.templateId);

            if (teleport != null)
            {
                if (IsAllowedToEnterPlace(character, house))
                {
                    EnterHouse(character, house, teleport);
                }
            }
        }

        /// <summary>
        /// Gets a name for building with certain id
        /// </summary>
        /// <param name="buildingId">id of the building that's name is wanted</param>
        /// <returns>Name of the building</returns>
        public String GetBuildingName(int buildingId)
        {
            if (buildingNames.ContainsKey(buildingId))
            {
                return buildingNames.Get(buildingId);
            }
            return "Building";
        }

        /// <summary>
        /// Gets a name for building where certain house template is
        /// </summary>
        /// <param name="templateId">House template id</param>
        /// <returns>Building name</returns>
        public String GetBuildingNameForHouseTemplateId(int templateId)
        {
            return GetBuildingName(GetBuildingIdForTemplateId(templateId));
        }

        /// <summary>
        /// Adds character inside a house with a certain ID
        /// </summary>
        /// <param name="id">House id</param>
        /// <param name="character">Character</param>
        public void AddCharacterToHouseWithId(int id, Character character)
        {
            House house = GetHouseForId(id);
            house.AddOccupant(character);
        }

        /// <summary>
        /// Removes a character from a house with certain ID
        /// </summary>
        /// <param name="id">House id</param>
        /// <param name="character">Character</param>
        public void RemoveCharacterFromHouseWithId(int id, Character character)
        {
            House house = GetHouseForId(id);
            house.RemoveOccupant(character);
        }

        /// <summary>
        /// Gets a name of house template
        /// </summary>
        /// <param name="templateId"></param>
        /// <returns></returns>
        public string GetHouseNameForTemplateId(int templateId)
        {
            return houseTemplates.Single(x => x.id == templateId).house_name;
        }

        /// <summary>
        /// Requests an exit from a certain place
        /// </summary>
        /// <param name="player">Sender of the request</param>
        /// <param name="teleportId">Id of teleport that is used</param>
        /// <param name="destinationId">Id of destination where to exit</param>
        public void RequestExitHouse(Character character, int teleportId, int destinationId)
        {
            Teleport teleport = this.GetExitTeleportForIdAndInRange(character, teleportId);
            if (teleport != null)
            {
                ExitHouse(character, teleport, destinationId);
            }
        }

        /// <summary>
        /// Gets spawn location for a house with certain id(normal id, not template id)
        /// </summary>
        /// <param name="houseId">House id</param>
        /// <returns>Coordinates of spawn point</returns>
        public Vector3 GetSpawnLocationOfHouseWithId(int houseId)
        {
            int templateId = GetTemplateIdForId(houseId);
            return houseTemplates.Single(h => h.id == templateId).spawn_position;
        }

        /// TODO: Add renting option
        /// <summary>
        /// Checks if character is owner or renter of a house
        /// </summary>
        /// <param name="houseId">House id</param>
        /// <returns>True if character is renter or owner, otherwise false</returns>
        public Boolean IsCharacterOwnerOrRenterOfHouse(Character character, int houseId)
        {
            House house = GetHouseForId(houseId);
            if (house == null || house.ownerId != character.ID)
            {
                return false;
            }
            return true;
        }

        public void TryBuyMarketHouseForCharacter(Character character, int houseSellId, string nameOfHouse)
        {
            this.houseMarket.TryBuyHouseForCharacter(character, houseSellId, nameOfHouse);
        }

        /// <summary>
        /// Adds a new house for player with selected template
        /// </summary>
        /// <param name="character">Character</param>
        /// <param name="templateId"></param>
        public void AddHouseOwnershipForCharacter(Character character, int templateId, string nameOfHouse)
        {
            DBManager.InsertQuery("INSERT INTO houses VALUES (@id, @ownerId, @templateId, @name)")
                .AddValue("@id", this.entryId + 1)
                .AddValue("@ownerId", character.ID)
                .AddValue("@templateId", templateId)
                .AddValue("@name", nameOfHouse)
                .Execute();
            this.AddHouse(this.entryId + 1, character.ID, templateId, nameOfHouse);
            this.SendListOfOwnedHousesToClient(character.owner.client);
        }


        /// <summary>
        /// Is triggered when character walks into teleport
        /// </summary>
        /// <param name="teleport">Teleport walked into</param>
        /// <param name="character">Character who walked into the teleport</param>
        public void EnterHouseTeleport(Checkpoint teleport, Character character)
        {
            if (!IsTimerActiveForPlayer(character))
            {
                Teleport tele = teleport as Teleport;
                // Make list of all houses the player can enter to (either own house, rents or is invited to house)
                List<House> apartments = GetPlacesAllowedToEnterInBuilding(character, tele.id); // currently only can enter apartments that he/she owns and where invited

                if (apartments.Count != 0)
                {
                    List<string> names = GetApartmentNames(apartments);
                    List<int> ids = GetApartmentIds(apartments);
                    API.shared.triggerClientEvent(character.owner.client, "EVENT_DISPLAY_ENTER_HOUSE_MENU", names, ids, GetBuildingName(tele.id));
                }
                else
                {
                    API.shared.sendNotificationToPlayer(character.owner.client, "There are no places you can enter in this building");
                }
            }
        }

        /// <summary>
        /// Is triggered when character walks into exit teleport
        /// </summary>
        /// <param name="teleport">Exit teleport walked into</param>
        /// <param name="character">Character who walked into the exit teleport</param>
        public void EnterHouseExitTeleport(Checkpoint teleport, Character character)
        {
            if (!IsTimerActiveForPlayer(character))
            {
                Teleport tele = teleport as Teleport;
                List<string> destNames = tele.GetDestinationNames();
                int teleportId = tele.id;
                List<int> destIds = tele.GetDestinationIds();

                API.shared.triggerClientEvent(character.owner.client, "EVENT_DISPLAY_EXIT_HOUSE_MENU", teleportId, destNames, destIds, GetBuildingName(tele.id));
            }
        }

        /// <summary>
        /// Loads everything needed for HouseManager initialization
        /// Has to be ran on script setup
        /// </summary>
        public void InitializeHouseManager()
        {
            DBManager.SelectQuery("SELECT * FROM house_template", (MySql.Data.MySqlClient.MySqlDataReader reader) =>
            {
                Vector3 spawn = new Vector3(reader.GetFloat(4), reader.GetFloat(5), reader.GetFloat(6));
                HouseTemplate h = new HouseTemplate(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), spawn);
                this.houseTemplates.Add(h);
                this.buildingIdForTemplateId.Add(reader.GetInt32(0), reader.GetInt32(1));
            }).Execute();

            // Load everything else
            CreateHouseTeleports();
            CreateHouseExitTeleports();
            LoadHousesFromDB();
            InitBuildings();
            InitHouseMarket();
            InitEvents();
        }

        /// <summary>
        /// Gets teleport in range for building template id
        /// </summary>
        /// <param name="character">Character for which to check</param>
        /// <param name="houseTemplateId">Template id of building</param>
        /// <returns>Teleport that is in range or null</returns>
        public Teleport GetInRangeTeleportForHouseTemplate(Character character, int houseTemplateId)
        {
            List<Teleport> teleports = houseTeleports.Where(t => t.id == GetBuildingIdForTemplateId(houseTemplateId) && t.IsCharacterInsideTeleport(character)).ToList();
            if (teleports.Count == 0)
            {
                return null;
            }

            return teleports.First();
        }

        /// <summary>
        /// Send list of owned places to client
        /// </summary>
        /// <param name="client">Character whose owned places are sent</param>
        public void SendListOfOwnedHousesToClient(Client client)
        {
            if (PlayerManager.Instance().IsClientUsingCharacter(client))
            {
                List<House> houses = GetOwnedHouses(PlayerManager.Instance().GetActiveCharacterForClient(client));
                API.shared.triggerClientEvent(client, "EVENT_SEND_OWNED_HOUSES", houses.Select(x => x.name).ToList<string>(), houses.Select(x => x.id).ToList<int>());
            }
        }
    }
}
