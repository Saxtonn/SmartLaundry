﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SmartLaundry.Authorization;
using SmartLaundry.Controllers.Helpers;
using SmartLaundry.Data.Interfaces;
using SmartLaundry.Models;
using SmartLaundry.Models.LaundryViewModels;

namespace SmartLaundry.Controllers
{
    [Authorize]
    public class LaundryController : Controller
    {
        private readonly ILaundryRepository _laundryRepo;
        private readonly IReservationRepository _reservationRepo;
        private readonly IUserRepository _userRepo;
        private readonly IWashingMachineRepository _washingMachineRepo;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuthorizationService _authorizationService;
        private readonly IDormitoryRepository _dormitoryRepo;

        private readonly AuthHelpers.AuthRepositories _authRepositories;

        public LaundryController(
            ILaundryRepository laundryRepository, IReservationRepository reservationRepository,
            IUserRepository userRepository, IWashingMachineRepository washingMachineRepo,
            UserManager<ApplicationUser> userManager, IAuthorizationService authorizationService,
            IDormitoryRepository dormitoryRepository)
        {
            _laundryRepo = laundryRepository;
            _reservationRepo = reservationRepository;
            _userRepo = userRepository;
            _userManager = userManager;
            _washingMachineRepo = washingMachineRepo;
            _authorizationService = authorizationService;
            _dormitoryRepo = dormitoryRepository;

            _authRepositories = new AuthHelpers.AuthRepositories()
            {
                DormitoryRepo = dormitoryRepository,
                AuthorizationService = authorizationService,
                WashingMachineRepo = washingMachineRepo,
                LaundryRepo = laundryRepository
            };
        }

        [HttpGet]
        [Authorize(Policy = "MinimumOccupant")]
        public IActionResult Index(int id)
        {
            return RedirectToDay(id, DateTime.Now.Date);
        }

        [HttpGet("/[controller]/[action]/{id}/{year}/{month}/{day}")]
        [Authorize(Policy = "MinimumOccupant")]
        public async Task<IActionResult> Day(int id, int year, int month, int day)
        {
            if(!await AuthHelpers.CheckDormitoryMembership(User, _authRepositories, id))
            {
                return ControllerHelpers.ShowAccessDeniedErrorPage(this);
            }

            var model = CreateDayViewModel(id, new DateTime(year, month, day));

            return View(model);
        }

        [Authorize(Policy = "MinimumOccupant")]
        public IActionResult Day(DayViewModel model)
        {
            return model.DormitoryId == 0 ? ControllerHelpers.Show404ErrorPage(this) : View(model);
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        [Authorize(Policy = "MinimumManager")]
        public async Task<IActionResult> AddLaundry(DayViewModel parentModel)
        {
            var laundry = parentModel.LaundryToAdd;

            if(!await AuthHelpers.CheckDormitoryMembership(User, _authRepositories, laundry.DormitoryId))
            {
                return ControllerHelpers.ShowAccessDeniedErrorPage(this);
            }

            DayViewModel model = null;
            var wholeWorkingTime = laundry.startTime + laundry.shiftTime * laundry.shiftCount;
            if (wholeWorkingTime.TotalHours > 24)
            {
                model = CreateDayViewModel(laundry.DormitoryId, DateTime.Today);
                model.AddLaundryError = "Laundry's working time ends after midnight.";
            } else if (_laundryRepo.Laundries.Any(x => x.DormitoryId == laundry.DormitoryId && x.Position == laundry.Position))
            {
                model = CreateDayViewModel(laundry.DormitoryId, DateTime.Today);
                model.AddLaundryError = "There is a laundry with the same number.";
            }

            if (model != null)
            { 
                model.LaundryToAdd.startTime = laundry.startTime;
                model.LaundryToAdd.shiftCount = laundry.shiftCount;
                model.LaundryToAdd.shiftTime = laundry.shiftTime;
                model.LaundryToAdd.Position = laundry.Position;
                model.LaundryToAdd.DormitoryId = laundry.DormitoryId;
                return View(nameof(Day), model);
            }

            var addedLaundry = _laundryRepo.AddLaundry(laundry.DormitoryId, laundry.Position);

            addedLaundry.startTime = laundry.startTime;
            addedLaundry.shiftCount = laundry.shiftCount;
            addedLaundry.shiftTime = laundry.shiftTime;

            _laundryRepo.UpdateLaundry(addedLaundry);

            return RedirectToDayByLaundryId(addedLaundry.Id, DateTime.Now.Date);
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        [Authorize(Policy = "MinimumManager")]
        public async Task<IActionResult> DeleteLaundry(int id)
        {
            var laundry = _laundryRepo.GetLaundryById(id);
            if(laundry == null)
            {
                return null;
            }

            var dormitoryId = _laundryRepo.GetLaundryById(laundry.Id).DormitoryId;

            if(!await AuthHelpers.CheckDormitoryMembership(User, _authRepositories, dormitoryId))
            {
                return ControllerHelpers.ShowAccessDeniedErrorPage(this);
            }

            _laundryRepo.RemoveLaundry(laundry);

            return RedirectToAction(nameof(Index), new { id = dormitoryId });
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        [Authorize(Policy = "MinimumManager")]
        public async Task<IActionResult> AddWashingMachine(DayViewModel parentModel)
        {
            var laundryId = parentModel.WashingMachineToAdd.LaundryId;
            var machinePosition = parentModel.WashingMachineToAdd.Position;

            var laundry = _laundryRepo.GetLaundryById(laundryId);
            if(!await AuthHelpers.CheckDormitoryMembership(User, _authRepositories, laundry.DormitoryId))
            {
                return ControllerHelpers.ShowAccessDeniedErrorPage(this);
            }

            if(_washingMachineRepo.WashingMachines.Any(x=>x.Position == machinePosition && x.LaundryId == laundryId))
            {
                var model = CreateDayViewModel(laundry.DormitoryId, DateTime.Today);
                model.AddWashingMachineError = "There is washing machine with the same number in this laundry.";
                model.WashingMachineToAdd.Position = laundry.Position;
                model.WashingMachineToAdd.LaundryId = laundryId;
                return View(nameof(Day), model);
            }

            var machine = _washingMachineRepo.AddWashingMachine(laundryId, machinePosition);

            return RedirectToDayByLaundryId(machine.LaundryId, DateTime.Now.Date);
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        [Authorize(Policy = "MinimumManager")]
        public async Task<IActionResult> RemoveWashingMachine(int machineId)
        {
            var machine = _washingMachineRepo.GetWashingMachineById(machineId);

            if(machine == null)
            {
                return ControllerHelpers.Show404ErrorPage(this);
            }

            if(!await AuthHelpers.CheckDormitoryMembership(User, _authRepositories, machine))
            {
                return ControllerHelpers.ShowAccessDeniedErrorPage(this);
            }

            _washingMachineRepo.RemoveWashingMachine(machine);
            return RedirectToDayByLaundryId(machine.LaundryId, DateTime.Now.Date);
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        [Authorize(Policy = "MinimumOccupant")]
        public async Task<IActionResult> CancelReservation(int reservationId)
        {
            var reservation = _reservationRepo.GetReservationById(reservationId);
            var machine = _washingMachineRepo.GetWashingMachineById(reservation.WashingMachineId);

            if(!await AuthHelpers.CheckDormitoryMembership(User, _authRepositories, reservation))
            {
                return ControllerHelpers.Show404ErrorPage(this);
            }

            var userId = _userManager.GetUserId(User);
            var roomId = _userRepo.GetUserById(userId).RoomId;

            if(_reservationRepo.GetHourReservation(reservation.WashingMachineId, reservation.StartTime) == null
                || _reservationRepo.IsFaultAtTime(reservation.WashingMachineId, reservation.StartTime)
                || reservation.StartTime < DateTime.Now
                || roomId != reservation.RoomId && roomId != null)
            {
                return ControllerHelpers.Show404ErrorPage(this);
            }

            // ReSharper disable once PossibleNullReferenceException
            _reservationRepo.RemoveReservation(reservation);

            return RedirectToDayByLaundryId(machine.LaundryId, reservation.StartTime);
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        [Authorize(Policy = "MinimumOccupant")]
        public async Task<IActionResult> Reserve(DateTime startTime, int machineId)
        {
            var machine = _washingMachineRepo.GetWashingMachineById(machineId);
            if(!await AuthHelpers.CheckDormitoryMembership(User, _authRepositories, machine))
            {
                return ControllerHelpers.ShowAccessDeniedErrorPage(this);
            }

            var userId = _userManager.GetUserId(User);
            var roomId = _userRepo.GetUserById(userId).RoomId;

            var reservation = new Reservation()
            {
                StartTime = startTime,
                RoomId = roomId,
                WashingMachineId = machineId,
                Fault = false
            };

            var reservationAtHour = _reservationRepo.GetHourReservation(machineId, startTime);
            var faultAtTime = _reservationRepo.IsFaultAtTime(machineId, startTime);

            if(reservationAtHour != null
                || faultAtTime
                || reservation.StartTime < DateTime.Now)
            {
                return ControllerHelpers.Show404ErrorPage(this);
            }

            if(_reservationRepo.IsCurrentlyFault(machineId))
            {
                return ControllerHelpers.Show404ErrorPage(this);
            }

            if(roomId != null)
            {
                var toRenew = _reservationRepo.GetRoomToRenewReservation(roomId.Value);
                var roomReservation = _reservationRepo.GetRoomDailyReservation(roomId.Value, startTime);
                if(toRenew != null)
                {
                    _reservationRepo.RemoveReservation(toRenew);
                }
                else if(roomReservation != null)
                {
                    return ControllerHelpers.Show404ErrorPage(this);
                }
            }

            _reservationRepo.AddReservation(reservation);

            return RedirectToDayByLaundryId(machine.LaundryId, startTime);
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        [Authorize(Policy = "MinimumPorter")]
        public async Task<IActionResult> EnableWashingMachine(int machineId)
        {
            var machine = _washingMachineRepo.GetWashingMachineById(machineId);
            if(machine == null)
            {
                return ControllerHelpers.Show404ErrorPage(this);
            }

            if(!await AuthHelpers.CheckDormitoryMembership(User, _authRepositories, machine))
            {
                return ControllerHelpers.ShowAccessDeniedErrorPage(this);
            }

            if(!_reservationRepo.IsCurrentlyFault(machineId))
            {
                return ControllerHelpers.Show404ErrorPage(this);
            }

            _washingMachineRepo.EnableWashingMachine(machineId);

            return RedirectToDayByLaundryId(machine.LaundryId, DateTime.Now.Date);
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        [Authorize(Policy = "MinimumPorter")]
        public async Task<IActionResult> DisableWashingMachine(int machineId)
        {
            var machine = _washingMachineRepo.GetWashingMachineById(machineId);
            if(machine == null)
            {
                return ControllerHelpers.Show404ErrorPage(this);
            }

            if(!await AuthHelpers.CheckDormitoryMembership(User, _authRepositories, machine))
            {
                return ControllerHelpers.ShowAccessDeniedErrorPage(this);
            }

            if(_reservationRepo.IsCurrentlyFault(machineId))
            {
                return ControllerHelpers.Show404ErrorPage(this);
            }

            _washingMachineRepo.DisableWashingMachine(machineId);

            return RedirectToDayByLaundryId(machine.LaundryId, DateTime.Now.Date);
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        [Authorize(Policy = "MinimumOccupant")]
        public async Task<IActionResult> ConfirmReservation(int reservationId)
        {
            var reservation = _reservationRepo.GetReservationById(reservationId);

            var machine = _washingMachineRepo.GetWashingMachineById(reservation.WashingMachineId);
            if(!await AuthHelpers.CheckDormitoryMembership(User, _authRepositories, machine))
            {
                return ControllerHelpers.ShowAccessDeniedErrorPage(this);
            }

            if(_reservationRepo.IsFaultAtTime(reservation.WashingMachineId, reservation.StartTime)
                || reservation.Confirmed
                || reservation.StartTime < DateTime.Now)
            {
                return ControllerHelpers.Show404ErrorPage(this);
            }

            reservation.ToRenew = false;
            _reservationRepo.UpdateReservation(reservation);

            return RedirectToDayByLaundryId(machine.LaundryId, reservation.StartTime.Date);
        }


        private IActionResult RedirectToDayByLaundryId(int laundryId, DateTime day)
        {
            var dormitoryId = _laundryRepo.GetLaundryById(laundryId).DormitoryId;
            return RedirectToDay(dormitoryId, day);
        }

        private IActionResult RedirectToDay(int dormitoryId, DateTime day)
        {
            return RedirectToAction(
                nameof(Day),
                new
                {
                    id = dormitoryId,
                    year = day.Year,
                    month = day.Month,
                    day = day.Day
                });
        }

        private DayViewModel CreateDayViewModel(int id, DateTime date)
        {
            var laundries = _laundryRepo.GetDormitoryLaundriesWithEntitiesAtDay(id, date) ?? new List<Laundry>();

            var userId = _userManager.GetUserId(User);
            var roomId = _userRepo.GetUserById(userId).RoomId;

            var model = new DayViewModel()
            {
                Laundries = laundries,
                DormitoryId = id,
                washingMachineState = _reservationRepo.GetDormitoryWashingMachineStates(id),
                date = date
            };

            if(roomId != null)
            {
                model.currentRoomReservation = _reservationRepo.GetRoomDailyReservation(roomId.Value, date);
                model.hasReservationToRenew = _reservationRepo.HasReservationToRenew(roomId.Value);
            }
            else
            {
                model.currentRoomReservation = null;
                model.hasReservationToRenew = false;
            }

            return model;
        }
    }
}