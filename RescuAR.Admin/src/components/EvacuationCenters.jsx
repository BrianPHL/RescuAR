import React, { useState, useEffect } from 'react';
import {
  Shield,
  Users,
  MapPin,
  Phone,
  Plus,
  Search,
  CheckCircle2,
  AlertTriangle,
  RefreshCw,
  Edit,
  Archive,
  X,
  ChevronLeft,
  ChevronRight,
  Navigation,
  Image as ImageIcon,
  Camera,
  Upload
} from 'lucide-react';
import { supabase } from '../supabaseClient';

export default function EvacuationCenters() {
  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [classificationFilter, setClassificationFilter] = useState('');
  const [selectedCenter, setSelectedCenter] = useState(null);
  const [isDrawerOpen, setIsDrawerOpen] = useState(false);
  const [isEditing, setIsEditing] = useState(false);
  const [loading, setLoading] = useState(true);
  const [isRefreshSpinning, setIsRefreshSpinning] = useState(false);
  const [currentPage, setCurrentPage] = useState(1);
  const [sortOrder, setSortOrder] = useState('name-asc');
  const itemsPerPage = 10;

  // Full official Marikina Evacuation Center data from official classifications (All 45 Centers)
  const defaultCenters = [
    // Flood-Safe Major Evacuation Centers
    {
      id: '1',
      name: 'Malanday Elementary School',
      barangay: 'Malanday',
      classification: 'Flood-Safe Major',
      longitude: '121.094409',
      latitude: '14.650283',
      capacity: 1200,
      currentEvacuees: 450,
      status: 'Open',
      headOfficer: 'Captain Roberto Santos',
      contact: '0917-555-0192',
      facilities: ['Medical Station', 'Clean Water', 'Generator', 'Modular Tents']
    },
    {
      id: '2',
      name: 'H. Bautista Elementary School',
      barangay: 'Concepcion Uno',
      classification: 'Flood-Safe Major',
      longitude: '121.104240',
      latitude: '14.657914',
      capacity: 1000,
      currentEvacuees: 200,
      status: 'Open',
      headOfficer: 'Kagawad Arnel Cruz',
      contact: '0918-444-9120',
      facilities: ['Medical Station', 'Clean Water', 'Restrooms']
    },
    {
      id: '3',
      name: 'Nangka Elementary School',
      barangay: 'Nangka',
      classification: 'Flood-Safe Major',
      longitude: '121.108440',
      latitude: '14.672991',
      capacity: 1500,
      currentEvacuees: 980,
      status: 'Open',
      headOfficer: 'Kagawad Manuel Reyes',
      contact: '0920-333-8101',
      facilities: ['Clean Water', 'Generator', 'Kitchen Area', 'Child-Friendly Space']
    },
    {
      id: '4',
      name: 'Concepcion Elementary School',
      barangay: 'Concepcion Uno',
      classification: 'Flood-Safe Major',
      longitude: '121.103974',
      latitude: '14.647648',
      capacity: 1100,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Officer Gabriel Fernandez',
      contact: '0917-222-3456',
      facilities: ['Generator', 'Restrooms', 'Clean Water']
    },
    {
      id: '5',
      name: 'Sto. Niño Elementary School',
      barangay: 'Sto. Niño',
      classification: 'Flood-Safe Major',
      longitude: '121.098368',
      latitude: '14.638324',
      capacity: 1300,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Maria Gonzales (MCDRRMO)',
      contact: '0915-222-7711',
      facilities: ['Generator', 'Restrooms', 'Parking', 'Medical Hub']
    },
    {
      id: '6',
      name: 'Sto. Niño National High School',
      barangay: 'Sto. Niño',
      classification: 'Flood-Safe Major',
      longitude: '121.096448',
      latitude: '14.638939',
      capacity: 1400,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'MDRRMO Relief Team Alpha',
      contact: '0917-809-5141',
      facilities: ['Major Relief Hub', 'Medical Station', 'Generator', 'Full Kitchen']
    },
    {
      id: '7',
      name: 'Leodegario Victorino Elementary',
      barangay: 'Jesus dela Peña',
      classification: 'Flood-Safe Major',
      longitude: '121.090214',
      latitude: '14.635407',
      capacity: 900,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Kagawad Carlos Mendoza',
      contact: '0919-666-4321',
      facilities: ['Clean Water', 'Restrooms']
    },
    {
      id: '8',
      name: 'Bulelak Gym',
      barangay: 'Malanday',
      classification: 'Flood-Safe Major',
      longitude: 'Not provided',
      latitude: 'Not provided',
      capacity: 650,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Kagawad Eric Castro',
      contact: '0917-444-9988',
      facilities: ['Covered Gym', 'Restrooms']
    },
    {
      id: '9',
      name: 'Sampaguita Gym',
      barangay: 'Malanday',
      classification: 'Flood-Safe Major',
      longitude: '124.998659*',
      latitude: '11.224286*',
      capacity: 700,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Officer Danilo Cruz',
      contact: '0918-333-7766',
      facilities: ['Covered Gym', 'Clean Water']
    },
    {
      id: '10',
      name: 'Marikina Elementary School',
      barangay: 'Sta. Elena',
      classification: 'Flood-Safe Major',
      longitude: '121.097529',
      latitude: '14.631281',
      capacity: 1250,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Capt. Antonio Flores',
      contact: '0917-333-8899',
      facilities: ['Medical Station', 'Clean Water', 'Generator']
    },
    {
      id: '11',
      name: 'Sta. Elena High School',
      barangay: 'Sta. Elena',
      classification: 'Flood-Safe Major',
      longitude: '121.097392',
      latitude: '14.632361',
      capacity: 1600,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Elena Soriano',
      contact: '0918-111-2233',
      facilities: ['Major Relief Hub', 'Clean Water', 'Generator']
    },
    {
      id: '12',
      name: 'Nangka Gym',
      barangay: 'Nangka',
      classification: 'Flood-Safe Major',
      longitude: '121.108408',
      latitude: '14.672471',
      capacity: 800,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Officer Reynaldo Diaz',
      contact: '0920-555-6677',
      facilities: ['Generator', 'Restrooms', 'Clean Water']
    },
    {
      id: '13',
      name: 'Kalumpang Elementary School',
      barangay: 'Kalumpang',
      classification: 'Flood-Safe Major',
      longitude: '121.090032',
      latitude: '14.622431',
      capacity: 1100,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Kagawad Joselito Ramos',
      contact: '0916-444-5566',
      facilities: ['Clean Water', 'Medical Station']
    },
    {
      id: '14',
      name: 'Kalumpang NHS',
      barangay: 'Kalumpang',
      classification: 'Flood-Safe Major',
      longitude: '121.089973',
      latitude: '14.622111',
      capacity: 1300,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Capt. Fernando Castro',
      contact: '0917-777-8822',
      facilities: ['Generator', 'Clean Water', 'Restrooms']
    },
    {
      id: '15',
      name: 'San Roque Elementary School',
      barangay: 'San Roque',
      classification: 'Flood-Safe Major',
      longitude: '121.096947',
      latitude: '14.623069',
      capacity: 1000,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Officer Danilo Gutierrez',
      contact: '0918-999-1122',
      facilities: ['Clean Water', 'Restrooms']
    },
    {
      id: '16',
      name: 'San Roque High School',
      barangay: 'San Roque',
      classification: 'Flood-Safe Major',
      longitude: '121.097204',
      latitude: '14.622760',
      capacity: 1500,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'MCDRRMO Team Bravo',
      contact: '0915-888-3344',
      facilities: ['Major Relief Hub', 'Medical Station', 'Generator']
    },
    {
      id: '17',
      name: 'Barangka Elementary School',
      barangay: 'Barangka',
      classification: 'Flood-Safe Major',
      longitude: '121.081934',
      latitude: '14.633421',
      capacity: 1050,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Capt. Mario Morales',
      contact: '0917-444-2211',
      facilities: ['Clean Water', 'Restrooms', 'Generator']
    },
    {
      id: '18',
      name: 'Tañong High School',
      barangay: 'Tañong',
      classification: 'Flood-Safe Major',
      longitude: '121.085333',
      latitude: '14.634177',
      capacity: 1200,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Kagawad Ricardo Navarro',
      contact: '0920-111-5544',
      facilities: ['Medical Station', 'Clean Water']
    },
    {
      id: '19',
      name: 'IVS Covered Court',
      barangay: 'Industrial Valley Complex',
      classification: 'Flood-Safe Major',
      longitude: '121.0850158',
      latitude: '14.6187095',
      capacity: 700,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Officer Sofia Valdez',
      contact: '0918-666-7788',
      facilities: ['Generator', 'Clean Water']
    },
    {
      id: '20',
      name: 'Jesus Dela Peña NHS',
      barangay: 'Jesus dela Peña',
      classification: 'Flood-Safe Major',
      longitude: '121.089906',
      latitude: '14.635212',
      capacity: 950,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Capt. Bernardo Aquino',
      contact: '0917-999-4455',
      facilities: ['Medical Station', 'Clean Water']
    },

    // Flood-Safe Minor Evacuation Centers
    {
      id: '21',
      name: 'St. Mary Elem. School',
      barangay: 'Parang',
      classification: 'Flood-Safe Minor',
      longitude: '121.113418',
      latitude: '14.668643',
      capacity: 600,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Officer Laura Reyes',
      contact: '0918-222-1100',
      facilities: ['Clean Water', 'Restrooms']
    },
    {
      id: '22',
      name: 'SSS Village Elem. School',
      barangay: 'Concepcion Dos',
      classification: 'Flood-Safe Minor',
      longitude: '121.121368',
      latitude: '14.640183',
      capacity: 800,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Capt. Ernesto Pineda',
      contact: '0917-111-9988',
      facilities: ['Generator', 'Clean Water']
    },
    {
      id: '23',
      name: 'SSS National High School',
      barangay: 'Concepcion Dos',
      classification: 'Flood-Safe Minor',
      longitude: '121.121304',
      latitude: '14.639743',
      capacity: 900,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Kagawad Teresa Santos',
      contact: '0920-888-7766',
      facilities: ['Clean Water', 'Restrooms']
    },
    {
      id: '24',
      name: 'Kap. Moy Elementary School',
      barangay: 'Concepcion Dos',
      classification: 'Flood-Safe Minor',
      longitude: '121.118689',
      latitude: '14.648958',
      capacity: 750,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Officer Manuel Villanueva',
      contact: '0915-444-3322',
      facilities: ['Clean Water', 'Restrooms']
    },
    {
      id: '25',
      name: 'Marikina Science High School',
      barangay: 'Sta. Elena',
      classification: 'Flood-Safe Minor',
      longitude: '121.099460',
      latitude: '14.631314',
      capacity: 850,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Capt. Rodrigo Cruz',
      contact: '0917-666-5544',
      facilities: ['Medical Station', 'Clean Water', 'Generator']
    },
    {
      id: '26',
      name: 'Champaca I Gym',
      barangay: 'Fortune',
      classification: 'Flood-Safe Minor',
      longitude: 'Not provided',
      latitude: 'Not provided',
      capacity: 500,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Officer Gabriel Morales',
      contact: '0917-123-9876',
      facilities: ['Restrooms', 'Clean Water']
    },
    {
      id: '27',
      name: 'Manotoc Gym',
      barangay: 'Sto. Niño',
      classification: 'Flood-Safe Minor',
      longitude: 'Not provided',
      latitude: 'Not provided',
      capacity: 450,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Kagawad Jose Garcia',
      contact: '0918-555-4321',
      facilities: ['Restrooms']
    },
    {
      id: '28',
      name: 'Sunny Square Gym',
      barangay: 'Fortune',
      classification: 'Flood-Safe Minor',
      longitude: '121.0985288',
      latitude: '14.6431288',
      capacity: 500,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Officer Beatrice Gomez',
      contact: '0918-333-2211',
      facilities: ['Clean Water', 'Restrooms']
    },
    {
      id: '29',
      name: 'Sta. Teresita Gym',
      barangay: 'Concepcion Dos',
      classification: 'Flood-Safe Minor',
      longitude: 'Not provided',
      latitude: 'Not provided',
      capacity: 480,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Officer Remedios Tan',
      contact: '0920-777-6611',
      facilities: ['Restrooms', 'Clean Water']
    },
    {
      id: '30',
      name: 'Amang Rodriguez Gym',
      barangay: 'Concepcion Uno',
      classification: 'Flood-Safe Minor',
      longitude: '121.103100',
      latitude: '14.652100',
      capacity: 650,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Capt. Ignacio Lopez',
      contact: '0920-444-1122',
      facilities: ['Generator', 'Clean Water']
    },
    {
      id: '31',
      name: 'St. Mary Gym',
      barangay: 'Parang',
      classification: 'Flood-Safe Minor',
      longitude: 'Not provided',
      latitude: 'Not provided',
      capacity: 520,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Officer Carmela Santos',
      contact: '0917-888-2211',
      facilities: ['Clean Water', 'Restrooms']
    },
    {
      id: '32',
      name: 'Fairlane Covered Court',
      barangay: 'Nangka',
      classification: 'Flood-Safe Minor',
      longitude: 'Not provided',
      latitude: 'Not provided',
      capacity: 550,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Kagawad Raul Ramos',
      contact: '0919-444-7788',
      facilities: ['Covered Court', 'Clean Water']
    },
    {
      id: '33',
      name: 'St. Benedick Gym, Nangka',
      barangay: 'Nangka',
      classification: 'Flood-Safe Minor',
      longitude: 'Not provided',
      latitude: 'Not provided',
      capacity: 500,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Officer Benedict Reyes',
      contact: '0915-333-9900',
      facilities: ['Restrooms']
    },
    {
      id: '34',
      name: 'Greenland Gym',
      barangay: 'Nangka',
      classification: 'Flood-Safe Minor',
      longitude: 'Not provided',
      latitude: 'Not provided',
      capacity: 480,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Capt. Hector Fernandez',
      contact: '0918-666-3322',
      facilities: ['Clean Water', 'Restrooms']
    },
    {
      id: '35',
      name: 'Jesus Dela Peña Gym',
      barangay: 'Jesus dela Peña',
      classification: 'Flood-Safe Minor',
      longitude: '121.087972',
      latitude: '14.636041',
      capacity: 550,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Kagawad Patricia Javier',
      contact: '0917-222-1133',
      facilities: ['Clean Water', 'Restrooms']
    },

    // Dual-Purpose Major Evacuation Centers
    {
      id: '36',
      name: 'Concepcion Integrated School ES',
      barangay: 'Concepcion Uno',
      classification: 'Dual-Purpose Major',
      longitude: '121.101893',
      latitude: '14.649954',
      capacity: 1800,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'MCDRRMO Campus Lead',
      contact: '0915-999-0011',
      facilities: ['Dual-Purpose Fields', 'Medical Hub', 'Generator', 'Clean Water']
    },
    {
      id: '37',
      name: 'Concepcion Integrated School SL',
      barangay: 'Concepcion Uno',
      classification: 'Dual-Purpose Major',
      longitude: '121.101893',
      latitude: '14.649954',
      capacity: 1700,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Officer Liza Aquino',
      contact: '0917-777-3344',
      facilities: ['Dual-Purpose Fields', 'Generator', 'Clean Water']
    },
    {
      id: '38',
      name: 'PLMAR (GH) Campus Grounds',
      barangay: 'Concepcion Uno',
      classification: 'Dual-Purpose Major',
      longitude: '121.106189',
      latitude: '14.658173',
      capacity: 2200,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'PLMAR Disaster Response Officer',
      contact: '0917-888-9900',
      facilities: ['Open Campus Grounds', 'Helipad', 'Full Kitchen', 'Generator']
    },
    {
      id: '39',
      name: 'Marikina High School Wide Fields',
      barangay: 'Concepcion Uno',
      classification: 'Dual-Purpose Major',
      longitude: '121.103239',
      latitude: '14.647058',
      capacity: 2000,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Capt. Alejandro Reyes',
      contact: '0918-777-6655',
      facilities: ['Wide Open Fields', 'Medical Station', 'Clean Water']
    },
    {
      id: '40',
      name: 'Parang Elem. School Open Grounds',
      barangay: 'Parang',
      classification: 'Dual-Purpose Major',
      longitude: '121.111283',
      latitude: '14.657072',
      capacity: 1600,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Kagawad Victoriano Lim',
      contact: '0920-666-4433',
      facilities: ['Open Grounds', 'Generator', 'Restrooms']
    },
    {
      id: '41',
      name: 'Parang High School Open Grounds',
      barangay: 'Parang',
      classification: 'Dual-Purpose Major',
      longitude: '121.112227',
      latitude: '14.663300',
      capacity: 1750,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Capt. Benjamin Ocampo',
      contact: '0917-555-4433',
      facilities: ['Open Grounds', 'Clean Water', 'Medical Station']
    },

    // Dual-Purpose Minor Evacuation Centers
    {
      id: '42',
      name: 'Fortune Elem. School Fields',
      barangay: 'Fortune',
      classification: 'Dual-Purpose Minor',
      longitude: 'Not provided',
      latitude: 'Not provided',
      capacity: 700,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Officer Sandra Lopez',
      contact: '0918-222-5566',
      facilities: ['Open Grounds', 'Restrooms']
    },
    {
      id: '43',
      name: 'Fortune High School Fields',
      barangay: 'Fortune',
      classification: 'Dual-Purpose Minor',
      longitude: 'Not provided',
      latitude: 'Not provided',
      capacity: 800,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Kagawad Dennis Perez',
      contact: '0920-999-1122',
      facilities: ['Open Fields', 'Clean Water']
    },
    {
      id: '44',
      name: 'PLMAR San Roque Plaza',
      barangay: 'San Roque',
      classification: 'Dual-Purpose Minor',
      longitude: 'Not provided',
      latitude: 'Not provided',
      capacity: 650,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'PLMAR Representative',
      contact: '0917-333-4411',
      facilities: ['Open Plaza', 'Clean Water']
    },
    {
      id: '45',
      name: 'Marikina Hotel Parking Grounds',
      barangay: 'Pio del Pilar',
      classification: 'Dual-Purpose Minor',
      longitude: '121.112182',
      latitude: '14.638203',
      capacity: 600,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Officer Claudia Delgado',
      contact: '0918-444-2233',
      facilities: ['Open Parking', 'Generator', 'Clean Water']
    },

    // Earthquake-Safe Minor Evacuation Centers
    {
      id: '46',
      name: 'Sta. Elena Chapel Plaza',
      barangay: 'Sta. Elena',
      classification: 'Earthquake-Safe Minor',
      longitude: 'Not provided',
      latitude: 'Not provided',
      capacity: 400,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Officer Teresa Cruz',
      contact: '0917-888-1122',
      facilities: ['Open Courtyard', 'Clean Water']
    },
    {
      id: '47',
      name: 'Aglipay Church Courtyard, Malanday',
      barangay: 'Malanday',
      classification: 'Earthquake-Safe Minor',
      longitude: 'Not provided',
      latitude: 'Not provided',
      capacity: 450,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Kagawad Mario Santos',
      contact: '0918-777-5544',
      facilities: ['Open Courtyard']
    },
    {
      id: '48',
      name: 'Aglipay Church Courtyard, Sto. Niño',
      barangay: 'Sto. Niño',
      classification: 'Earthquake-Safe Minor',
      longitude: 'Not provided',
      latitude: 'Not provided',
      capacity: 450,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'Officer Vicente Ramos',
      contact: '0920-555-4433',
      facilities: ['Open Courtyard', 'Restrooms']
    },
    {
      id: '49',
      name: 'OLA Church Open Plaza & Courtyard',
      barangay: 'Sta. Elena',
      classification: 'Earthquake-Safe Minor',
      longitude: 'Not provided',
      latitude: 'Not provided',
      capacity: 600,
      currentEvacuees: 0,
      status: 'Standby',
      headOfficer: 'MCDRRMO Parish Coordinator',
      contact: '0915-444-2299',
      facilities: ['Open Plaza', 'Clean Water', 'Medical Station']
    }
  ];

  const [centers, setCenters] = useState(defaultCenters);

  const [formData, setFormData] = useState({
    id: null,
    name: '',
    barangay: '',
    classification: 'Flood-Safe Major',
    longitude: '',
    latitude: '',
    capacity: '',
    currentEvacuees: 0,
    status: 'Standby',
    headOfficer: '',
    contact: '',
    facilities: '',
    imageUrl: ''
  });

  const handleImageFileChange = (e) => {
    const file = e.target.files[0];
    if (file) {
      const reader = new FileReader();
      reader.onloadend = () => {
        setFormData(prev => ({ ...prev, imageUrl: reader.result }));
      };
      reader.readAsDataURL(file);
    }
  };

  const fetchCenters = async () => {
    setLoading(true);
    try {
      const { data, error } = await supabase
        .from('evacuation_centers')
        .select('*')
        .order('name', { ascending: true });

      if (error) {
        console.warn('Supabase fetch notice (evacuation_centers):', error.message);
      } else if (data && data.length > 0) {
        const mapped = data.map(item => ({
          id: item.id,
          name: item.name,
          barangay: item.barangay || 'Marikina',
          classification: item.classification || 'Flood-Safe Major',
          longitude: item.longitude || 'N/A',
          latitude: item.latitude || 'N/A',
          capacity: item.capacity || 500,
          currentEvacuees: item.current_evacuees || 0,
          status: item.status || 'Standby',
          headOfficer: item.head_officer || 'Unassigned',
          contact: item.contact || 'N/A',
          imageUrl: item.image_url || null,
          facilities: Array.isArray(item.facilities)
            ? item.facilities
            : typeof item.facilities === 'string'
              ? item.facilities.split(',').map(s => s.trim()).filter(Boolean)
              : ['Clean Water', 'Restrooms']
        }));
        setCenters(mapped);
        if (!selectedCenter) {
          setSelectedCenter(mapped[0]);
        }
      } else {
        if (!selectedCenter && defaultCenters.length > 0) {
          setSelectedCenter(defaultCenters[0]);
        }
      }
    } catch (err) {
      console.warn('Supabase client error:', err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchCenters();

    const channel = supabase
      .channel('evac-centers-db-changes')
      .on('postgres_changes', { event: '*', schema: 'public', table: 'evacuation_centers' }, () => {
        fetchCenters();
      })
      .subscribe();

    return () => {
      supabase.removeChannel(channel);
    };
  }, []);

  const handleRefresh = async () => {
    setIsRefreshSpinning(true);
    await fetchCenters();
    setTimeout(() => {
      setIsRefreshSpinning(false);
    }, 500);
  };

  const filteredCenters = centers.filter(c => {
    const matchesSearch = c.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
      c.barangay.toLowerCase().includes(searchTerm.toLowerCase()) ||
      (c.classification && c.classification.toLowerCase().includes(searchTerm.toLowerCase())) ||
      c.headOfficer.toLowerCase().includes(searchTerm.toLowerCase());
    const matchesStatus = statusFilter ? c.status === statusFilter : true;
    const matchesClassification = classificationFilter ? c.classification === classificationFilter : true;
    return matchesSearch && matchesStatus && matchesClassification;
  });

  const sortedCenters = [...filteredCenters].sort((a, b) => {
    if (sortOrder === 'name-asc') {
      return a.name.localeCompare(b.name);
    } else if (sortOrder === 'name-desc') {
      return b.name.localeCompare(a.name);
    } else if (sortOrder === 'barangay-asc') {
      return a.barangay.localeCompare(b.barangay);
    } else if (sortOrder === 'classification-asc') {
      return (a.classification || '').localeCompare(b.classification || '');
    }
    return 0;
  });

  // Reset to page 1 whenever filters, search query, or sort order change
  useEffect(() => {
    setCurrentPage(1);
  }, [searchTerm, statusFilter, classificationFilter, sortOrder]);

  const totalPages = Math.max(1, Math.ceil(sortedCenters.length / itemsPerPage));
  const paginatedCenters = sortedCenters.slice(
    (currentPage - 1) * itemsPerPage,
    currentPage * itemsPerPage
  );

  const openAddModal = () => {
    setIsEditing(false);
    setFormData({
      id: null,
      name: '',
      barangay: '',
      classification: 'Flood-Safe Major',
      longitude: '',
      latitude: '',
      status: 'Standby',
      headOfficer: '',
      contact: '',
      facilities: 'Clean Water, Restrooms, Generator',
      imageUrl: ''
    });
    setIsDrawerOpen(true);
  };

  const openEditModal = (center) => {
    setIsEditing(true);
    setFormData({
      id: center.id,
      name: center.name,
      barangay: center.barangay,
      classification: center.classification || 'Flood-Safe Major',
      longitude: center.longitude || '',
      latitude: center.latitude || '',
      status: center.status,
      headOfficer: center.headOfficer,
      contact: center.contact,
      facilities: Array.isArray(center.facilities) ? center.facilities.join(', ') : center.facilities || '',
      imageUrl: center.imageUrl || ''
    });
    setIsDrawerOpen(true);
  };

  const handleSave = async () => {
    if (!formData.name || !formData.barangay) {
      alert('Please provide a shelter name and barangay location.');
      return;
    }

    const facilitiesArray = formData.facilities
      ? formData.facilities.split(',').map(s => s.trim()).filter(Boolean)
      : ['Clean Water', 'Restrooms'];

    const payload = {
      name: formData.name,
      barangay: formData.barangay,
      classification: formData.classification,
      longitude: formData.longitude || null,
      latitude: formData.latitude || null,
      status: formData.status,
      head_officer: formData.headOfficer || 'Unassigned',
      contact: formData.contact || 'N/A',
      facilities: facilitiesArray,
      image_url: formData.imageUrl || null
    };

    if (isEditing && formData.id) {
      const { error } = await supabase.from('evacuation_centers').update(payload).eq('id', formData.id);
      if (error) {
        console.warn('Updating local state due to Supabase notice:', error.message);
        setCenters(prev => prev.map(item => item.id === formData.id ? { ...item, ...payload, facilities: facilitiesArray, headOfficer: payload.head_officer, imageUrl: payload.image_url } : item));
      } else {
        fetchCenters();
      }
    } else {
      const { error } = await supabase.from('evacuation_centers').insert([payload]);
      if (error) {
        console.warn('Adding to local state due to Supabase notice:', error.message);
        const newLocalCenter = {
          id: String(Date.now()),
          ...payload,
          facilities: facilitiesArray,
          headOfficer: payload.head_officer,
          imageUrl: payload.image_url
        };
        setCenters(prev => [...prev, newLocalCenter]);
        setSelectedCenter(newLocalCenter);
      } else {
        fetchCenters();
      }
    }

    setIsDrawerOpen(false);
  };

  const getStatusBadge = (status) => {
    switch (status) {
      case 'Open':
        return { bg: '#ecfdf5', color: '#059669', label: 'Open' };
      case 'Standby':
        return { bg: '#eff6ff', color: '#3b82f6', label: 'Standby' };
      case 'Full':
        return { bg: '#fef2f2', color: '#dc2626', label: 'Full' };
      default:
        return { bg: '#f1f5f9', color: '#64748b', label: status || 'Standby' };
    }
  };

  const styles = {
    panelContainer: { display: 'flex', gap: '20px', alignItems: 'flex-start' },
    leftPanel: { flex: '1', backgroundColor: '#fff', borderRadius: '8px', boxShadow: '0 1px 3px rgba(0,0,0,0.1)', overflow: 'hidden', minHeight: '600px', display: 'flex', flexDirection: 'column' },
    rightPanel: { width: '380px', backgroundColor: 'transparent', flexShrink: 0 },
    headerFlex: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '20px 20px 15px' },
    controlsFlex: { display: 'flex', gap: '10px', padding: '0 20px 15px' },
    searchInput: { flex: 1, padding: '8px 12px', borderRadius: '6px', border: '1px solid #e2e8f0', fontSize: '13px' },
    selectInput: { width: '130px', padding: '8px 12px', borderRadius: '6px', border: '1px solid #e2e8f0', fontSize: '13px', backgroundColor: '#fff' },
    addButton: { backgroundColor: '#0d9488', color: '#fff', border: 'none', padding: '8px 16px', borderRadius: '6px', fontSize: '13px', fontWeight: '600', display: 'flex', alignItems: 'center', gap: '6px', cursor: 'pointer' },
    tableHeader: { backgroundColor: '#f8fafc', padding: '12px 20px', borderBottom: '1px solid #e2e8f0', textAlign: 'left', fontSize: '12px', fontWeight: '600', color: '#64748b' },
    tableRow: { borderBottom: '1px solid #f1f5f9', cursor: 'pointer', transition: 'background 0.2s' },
    tableCell: { padding: '14px 20px', fontSize: '13px', color: '#334155' },
    pagination: { marginTop: 'auto', padding: '15px 20px', borderTop: '1px solid #e2e8f0', display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '12px', color: '#94a3b8' },
    pageControls: { display: 'flex', gap: '5px' },
    pageBtn: { padding: '4px 8px', border: '1px solid #e2e8f0', borderRadius: '4px', backgroundColor: '#fff', cursor: 'pointer', color: '#64748b', display: 'flex', alignItems: 'center' },

    detailsCard: { backgroundColor: '#fff', borderRadius: '8px', boxShadow: '0 1px 3px rgba(0,0,0,0.1)', padding: '20px' },
    sectionLabel: { fontSize: '12px', fontWeight: '500', color: '#94a3b8', marginBottom: '12px', marginTop: '16px', textTransform: 'uppercase', letterSpacing: '0.5px' },
    rowPair: { display: 'flex', justifyContent: 'space-between', marginBottom: '10px', fontSize: '13px' },
    label: { color: '#64748b' },
    val: { color: '#0f172a', fontWeight: '500', textAlign: 'right', maxWidth: '60%', wordBreak: 'break-word', lineHeight: '1.4' },
    outlineBtn: { width: '100%', padding: '10px', border: '1px solid #cbd5e1', borderRadius: '6px', backgroundColor: 'transparent', display: 'flex', justifyContent: 'center', alignItems: 'center', gap: '8px', cursor: 'pointer', fontSize: '13px', fontWeight: '600', color: '#334155', marginTop: '12px' },
    emptyState: { display: 'flex', justifyContent: 'center', alignItems: 'center', height: '600px', fontSize: '14px', color: '#94a3b8', fontWeight: '500', textAlign: 'center' },

    overlay: { position: 'fixed', top: 0, left: 0, right: 0, bottom: 0, backgroundColor: 'rgba(0,0,0,0.4)', zIndex: 999, display: 'flex', justifyContent: 'flex-end' },
    drawer: { width: '400px', backgroundColor: '#fff', height: '100%', display: 'flex', flexDirection: 'column', boxShadow: '-4px 0 15px rgba(0,0,0,0.1)' },
    drawerHeader: { padding: '20px', borderBottom: '1px solid #e2e8f0', display: 'flex', justifyContent: 'space-between', alignItems: 'center' },
    drawerBody: { padding: '20px', overflowY: 'auto', flex: 1, display: 'flex', flexDirection: 'column', gap: '16px' },
    drawerFooter: { padding: '20px', borderTop: '1px solid #e2e8f0', display: 'flex', justifyContent: 'flex-end', gap: '12px' },
    formGroup: { display: 'flex', flexDirection: 'column', gap: '6px' },
    input: { padding: '10px 12px', border: '1px solid #cbd5e1', borderRadius: '6px', fontSize: '13px', outline: 'none' }
  };

  const activeCenter = selectedCenter || filteredCenters[0] || centers[0];

  return (
    <div className="main-view" style={{ backgroundColor: '#f1f5f9', minHeight: '100vh', padding: '30px' }}>

      {/* PAGE HEADER */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px' }}>
        <div>
          <h1 style={{ fontSize: '24px', fontWeight: '700', color: '#0f172a', margin: '0 0 4px 0' }}>Evacuation Centers</h1>
          <span style={{ fontSize: '12px', color: '#94a3b8' }}>
            Monitor shelter capacity, classifications, coordinates, and assigned LGU relief officers across Marikina
          </span>
        </div>
        <button
          className="btn-refresh"
          onClick={handleRefresh}
          style={{ display: 'flex', alignItems: 'center', gap: '6px', padding: '8px 14px', borderRadius: '6px', border: '1px solid #cbd5e1', backgroundColor: '#fff', cursor: 'pointer', fontSize: '13px', color: '#334155' }}
        >
          <RefreshCw size={13} className={isRefreshSpinning ? 'spin-icon' : ''} />
          <span>Refresh</span>
        </button>
      </div>

      {/* SPLIT PANEL LAYOUT */}
      <div style={styles.panelContainer}>

        {/* LEFT PANEL: LIST OF EVACUATION CENTERS */}
        <div style={styles.leftPanel}>
          <div style={styles.headerFlex}>
            <h2 style={{ fontSize: '16px', fontWeight: '600', color: '#0f172a', margin: 0 }}>List of Evacuation Centers</h2>
            <button style={styles.addButton} onClick={openAddModal}>
              <Plus size={16} />
              <span>Add New Center</span>
            </button>
          </div>

          <div style={styles.controlsFlex}>
            <input
              type="text"
              placeholder="Search for an evacuation center..."
              style={styles.searchInput}
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
            />
            <select
              style={{ ...styles.selectInput, width: '150px' }}
              value={classificationFilter}
              onChange={(e) => setClassificationFilter(e.target.value)}
            >
              <option value="">Classification...</option>
              <option value="Flood-Safe Major">Flood-Safe Major</option>
              <option value="Flood-Safe Minor">Flood-Safe Minor</option>
              <option value="Dual-Purpose Major">Dual-Purpose Major</option>
              <option value="Dual-Purpose Minor">Dual-Purpose Minor</option>
              <option value="Earthquake-Safe Minor">Earthquake-Safe Minor</option>
            </select>
            <select
              style={styles.selectInput}
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
            >
              <option value="">Status...</option>
              <option value="Open">Open</option>
              <option value="Standby">Standby</option>
              <option value="Full">Full</option>
            </select>
            <select
              style={{ ...styles.selectInput, width: '155px' }}
              value={sortOrder}
              onChange={(e) => setSortOrder(e.target.value)}
            >
              <option value="name-asc">Sort: A to Z (Name)</option>
              <option value="name-desc">Sort: Z to A (Name)</option>
              <option value="barangay-asc">Sort: Barangay (A-Z)</option>
              <option value="classification-asc">Sort: Classification</option>
            </select>
          </div>

          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr>
                <th
                  style={{ ...styles.tableHeader, cursor: 'pointer', userSelect: 'none' }}
                  onClick={() => setSortOrder(prev => prev === 'name-asc' ? 'name-desc' : 'name-asc')}
                >
                  Shelter Name {sortOrder === 'name-asc' ? '↑' : sortOrder === 'name-desc' ? '↓' : ''}
                </th>
                <th
                  style={{ ...styles.tableHeader, cursor: 'pointer', userSelect: 'none' }}
                  onClick={() => setSortOrder('barangay-asc')}
                >
                  Barangay {sortOrder === 'barangay-asc' ? '↑' : ''}
                </th>
                <th
                  style={{ ...styles.tableHeader, cursor: 'pointer', userSelect: 'none' }}
                  onClick={() => setSortOrder('classification-asc')}
                >
                  Classification {sortOrder === 'classification-asc' ? '↑' : ''}
                </th>
                <th style={styles.tableHeader}>Status</th>
              </tr>
            </thead>
            <tbody>
              {paginatedCenters.map((c) => {
                const badge = getStatusBadge(c.status);
                const isSelected = activeCenter && activeCenter.id === c.id;

                return (
                  <tr
                    key={c.id}
                    style={{
                      ...styles.tableRow,
                      backgroundColor: isSelected ? '#f8fafc' : 'transparent'
                    }}
                    onClick={() => setSelectedCenter(c)}
                  >
                    <td style={{ ...styles.tableCell, fontWeight: '600', color: '#0f172a' }}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                        {c.imageUrl ? (
                          <img
                            src={c.imageUrl}
                            alt={c.name}
                            style={{ width: '32px', height: '32px', borderRadius: '6px', objectFit: 'cover', flexShrink: 0, border: '1px solid #e2e8f0' }}
                          />
                        ) : (
                          <div style={{ width: '32px', height: '32px', borderRadius: '6px', backgroundColor: '#f1f5f9', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0, border: '1px solid #e2e8f0' }}>
                            <Shield size={16} color="#64748b" />
                          </div>
                        )}
                        <span>{c.name}</span>
                      </div>
                    </td>
                    <td style={styles.tableCell}>{c.barangay}</td>
                    <td style={{ ...styles.tableCell, fontSize: '11px', color: '#64748b' }}>{c.classification || 'Flood-Safe Major'}</td>
                    <td style={styles.tableCell}>
                      <span style={{
                        backgroundColor: badge.bg,
                        color: badge.color,
                        padding: '3px 8px',
                        borderRadius: '12px',
                        fontSize: '11px',
                        fontWeight: '700'
                      }}>
                        {badge.label}
                      </span>
                    </td>
                  </tr>
                );
              })}
              {filteredCenters.length === 0 && (
                <tr>
                  <td colSpan="4" style={{ padding: '30px', textAlign: 'center', color: '#94a3b8', fontSize: '13px' }}>
                    No evacuation centers found.
                  </td>
                </tr>
              )}
            </tbody>
          </table>

          <div style={styles.pagination}>
            <span>
              Showing {filteredCenters.length > 0 ? (currentPage - 1) * itemsPerPage + 1 : 0} to {Math.min(currentPage * itemsPerPage, filteredCenters.length)} of {filteredCenters.length} evacuation centers
            </span>
            <div style={styles.pageControls}>
              <button
                style={{
                  ...styles.pageBtn,
                  opacity: currentPage === 1 ? 0.4 : 1,
                  cursor: currentPage === 1 ? 'not-allowed' : 'pointer'
                }}
                disabled={currentPage === 1}
                onClick={() => setCurrentPage(prev => Math.max(1, prev - 1))}
              >
                <ChevronLeft size={14} />
              </button>

              {Array.from({ length: totalPages }, (_, i) => i + 1).map(page => (
                <button
                  key={page}
                  style={{
                    ...styles.pageBtn,
                    backgroundColor: page === currentPage ? '#0d9488' : '#fff',
                    color: page === currentPage ? '#fff' : '#64748b',
                    borderColor: page === currentPage ? '#0d9488' : '#e2e8f0',
                    fontWeight: page === currentPage ? '700' : '500'
                  }}
                  onClick={() => setCurrentPage(page)}
                >
                  {page}
                </button>
              ))}

              <button
                style={{
                  ...styles.pageBtn,
                  opacity: currentPage === totalPages ? 0.4 : 1,
                  cursor: currentPage === totalPages ? 'not-allowed' : 'pointer'
                }}
                disabled={currentPage === totalPages}
                onClick={() => setCurrentPage(prev => Math.min(totalPages, prev + 1))}
              >
                <ChevronRight size={14} />
              </button>
            </div>
          </div>
        </div>

        {/* RIGHT PANEL: EVACUATION CENTER DETAILS */}
        <div style={styles.rightPanel}>
          {activeCenter ? (
            <div style={styles.detailsCard}>
              <h2 style={{ fontSize: '16px', fontWeight: '600', color: '#0f172a', margin: '0 0 14px 0' }}>Evacuation Center Details</h2>

              {/* SHELTER HERO PHOTO BANNER */}
              {activeCenter.imageUrl ? (
                <div style={{ position: 'relative', width: '100%', height: '150px', borderRadius: '8px', overflow: 'hidden', marginBottom: '16px', border: '1px solid #e2e8f0' }}>
                  <img
                    src={activeCenter.imageUrl}
                    alt={activeCenter.name}
                    style={{ width: '100%', height: '100%', objectFit: 'cover' }}
                  />
                  <button
                    onClick={() => openEditModal(activeCenter)}
                    style={{
                      position: 'absolute',
                      top: '8px',
                      right: '8px',
                      backgroundColor: 'rgba(15, 23, 42, 0.75)',
                      color: '#fff',
                      border: 'none',
                      borderRadius: '4px',
                      padding: '4px 8px',
                      fontSize: '11px',
                      fontWeight: '600',
                      display: 'flex',
                      alignItems: 'center',
                      gap: '4px',
                      cursor: 'pointer'
                    }}
                  >
                    <Camera size={12} />
                    <span>Change Photo</span>
                  </button>
                </div>
              ) : (
                <div style={{ width: '100%', height: '110px', borderRadius: '8px', backgroundColor: '#f8fafc', border: '2px dashed #cbd5e1', display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: '6px', marginBottom: '16px' }}>
                  <Camera size={22} color="#94a3b8" />
                  <span style={{ fontSize: '12px', color: '#64748b', fontWeight: '500' }}>No shelter photo added</span>
                  <button
                    onClick={() => openEditModal(activeCenter)}
                    style={{ fontSize: '11px', color: '#0d9488', fontWeight: '600', background: 'none', border: 'none', cursor: 'pointer', textDecoration: 'underline' }}
                  >
                    + Add Shelter Photo
                  </button>
                </div>
              )}

              <div style={styles.sectionLabel}>SHELTER INFORMATION</div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Shelter Name</span>
                <span style={styles.val}>{activeCenter.name}</span>
              </div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Barangay Location</span>
                <span style={styles.val}>{activeCenter.barangay}</span>
              </div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Classification</span>
                <span style={{ ...styles.val, color: '#0369a1' }}>{activeCenter.classification || 'Flood-Safe Major'}</span>
              </div>
              <div style={styles.rowPair}>
                <span style={styles.label}>GPS Coordinates</span>
                <span style={{ ...styles.val, fontSize: '11px', fontFamily: 'monospace' }}>
                  {activeCenter.latitude && activeCenter.longitude && activeCenter.latitude !== 'N/A'
                    ? `${activeCenter.latitude}, ${activeCenter.longitude}`
                    : 'Not provided'}
                </span>
              </div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Operating Status</span>
                <span style={{
                  ...styles.val,
                  color: getStatusBadge(activeCenter.status).color,
                  fontWeight: '700'
                }}>
                  {getStatusBadge(activeCenter.status).label}
                </span>
              </div>

              <div style={styles.sectionLabel}>OFFICER IN CHARGE</div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Head Officer</span>
                <span style={styles.val}>{activeCenter.headOfficer}</span>
              </div>
              <div style={styles.rowPair}>
                <span style={styles.label}>Contact Number</span>
                <span style={styles.val}>{activeCenter.contact}</span>
              </div>

              <div style={styles.sectionLabel}>FACILITIES & AMENITIES</div>
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: '6px', marginBottom: '16px' }}>
                {Array.isArray(activeCenter.facilities) ? activeCenter.facilities.map((fac, idx) => (
                  <span key={idx} style={{ backgroundColor: '#f1f5f9', color: '#334155', padding: '4px 10px', borderRadius: '16px', fontSize: '11px', fontWeight: '600', border: '1px solid #e2e8f0' }}>
                    {fac}
                  </span>
                )) : (
                  <span style={{ fontSize: '12px', color: '#94a3b8' }}>Standard Relief Facilities</span>
                )}
              </div>

              <button style={styles.outlineBtn} onClick={() => openEditModal(activeCenter)}>
                <Edit size={14} />
                <span>Edit Center Details</span>
              </button>
            </div>
          ) : (
            <div style={styles.detailsCard}>
              <div style={styles.emptyState}>Select an evacuation center to view details</div>
            </div>
          )}
        </div>
      </div>

      {/* DRAWER MODAL FOR ADDING / EDITING CENTER */}
      {isDrawerOpen && (
        <div style={styles.overlay}>
          <div style={styles.drawer}>
            <div style={styles.drawerHeader}>
              <h3 style={{ margin: 0, fontSize: '16px', fontWeight: '600', color: '#0f172a' }}>
                {isEditing ? 'Edit Evacuation Center' : 'Add New Evacuation Center'}
              </h3>
              <button
                onClick={() => setIsDrawerOpen(false)}
                style={{ background: 'none', border: 'none', cursor: 'pointer', color: '#64748b' }}
              >
                <X size={18} />
              </button>
            </div>

            <div style={styles.drawerBody}>

              {/* IMAGE UPLOAD & PREVIEW FORM GROUP */}
              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#64748b' }}>Shelter Photo / Image</label>

                {formData.imageUrl && (
                  <div style={{ position: 'relative', width: '100%', height: '120px', borderRadius: '6px', overflow: 'hidden', marginBottom: '8px', border: '1px solid #cbd5e1' }}>
                    <img src={formData.imageUrl} alt="Shelter Preview" style={{ width: '100%', height: '100%', objectFit: 'cover' }} />
                    <button
                      type="button"
                      onClick={() => setFormData({ ...formData, imageUrl: '' })}
                      style={{ position: 'absolute', top: '6px', right: '6px', backgroundColor: 'rgba(239, 68, 68, 0.9)', color: '#fff', border: 'none', borderRadius: '50%', width: '22px', height: '22px', display: 'flex', alignItems: 'center', justifyContent: 'center', cursor: 'pointer' }}
                    >
                      <X size={12} />
                    </button>
                  </div>
                )}

                <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
                  <label style={{
                    padding: '8px 12px',
                    backgroundColor: '#f1f5f9',
                    border: '1px solid #cbd5e1',
                    borderRadius: '6px',
                    fontSize: '12px',
                    fontWeight: '600',
                    color: '#334155',
                    cursor: 'pointer',
                    display: 'flex',
                    alignItems: 'center',
                    gap: '6px',
                    whiteSpace: 'nowrap'
                  }}>
                    <Upload size={14} />
                    <span>Upload Photo</span>
                    <input
                      type="file"
                      accept="image/*"
                      style={{ display: 'none' }}
                      onChange={handleImageFileChange}
                    />
                  </label>
                  <input
                    type="text"
                    placeholder="or paste image URL (https://...)"
                    style={{ ...styles.input, flex: 1 }}
                    value={formData.imageUrl}
                    onChange={(e) => setFormData({ ...formData, imageUrl: e.target.value })}
                  />
                </div>
              </div>

              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#64748b' }}>Shelter Name</label>
                <input
                  type="text"
                  placeholder="e.g. Malanday Elementary School"
                  style={styles.input}
                  value={formData.name}
                  onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                />
              </div>

              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#64748b' }}>Barangay Location</label>
                <input
                  type="text"
                  placeholder="e.g. Malanday"
                  style={styles.input}
                  value={formData.barangay}
                  onChange={(e) => setFormData({ ...formData, barangay: e.target.value })}
                />
              </div>

              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#64748b' }}>Classification</label>
                <select
                  style={styles.input}
                  value={formData.classification}
                  onChange={(e) => setFormData({ ...formData, classification: e.target.value })}
                >
                  <option value="Flood-Safe Major">Flood-Safe Major Evacuation Center</option>
                  <option value="Flood-Safe Minor">Flood-Safe Minor Evacuation Center</option>
                  <option value="Dual-Purpose Major">Dual-Purpose Major Evacuation Center</option>
                  <option value="Dual-Purpose Minor">Dual-Purpose Minor Evacuation Center</option>
                  <option value="Earthquake-Safe Minor">Earthquake-Safe Minor Evacuation Center</option>
                </select>
              </div>

              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '10px' }}>
                <div style={styles.formGroup}>
                  <label style={{ fontSize: '12px', fontWeight: '500', color: '#64748b' }}>Latitude</label>
                  <input
                    type="text"
                    placeholder="14.650283"
                    style={styles.input}
                    value={formData.latitude}
                    onChange={(e) => setFormData({ ...formData, latitude: e.target.value })}
                  />
                </div>
                <div style={styles.formGroup}>
                  <label style={{ fontSize: '12px', fontWeight: '500', color: '#64748b' }}>Longitude</label>
                  <input
                    type="text"
                    placeholder="121.094409"
                    style={styles.input}
                    value={formData.longitude}
                    onChange={(e) => setFormData({ ...formData, longitude: e.target.value })}
                  />
                </div>
              </div>

              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#64748b' }}>Operating Status</label>
                <select
                  style={styles.input}
                  value={formData.status}
                  onChange={(e) => setFormData({ ...formData, status: e.target.value })}
                >
                  <option value="Open">Open</option>
                  <option value="Standby">Standby</option>
                  <option value="Full">Full</option>
                </select>
              </div>

              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#64748b' }}>Officer In Charge</label>
                <input
                  type="text"
                  placeholder="e.g. Captain Roberto Santos"
                  style={styles.input}
                  value={formData.headOfficer}
                  onChange={(e) => setFormData({ ...formData, headOfficer: e.target.value })}
                />
              </div>

              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#64748b' }}>Contact Hotline Number</label>
                <input
                  type="text"
                  placeholder="e.g. 0917-555-0192"
                  style={styles.input}
                  value={formData.contact}
                  onChange={(e) => setFormData({ ...formData, contact: e.target.value })}
                />
              </div>

              <div style={styles.formGroup}>
                <label style={{ fontSize: '12px', fontWeight: '500', color: '#64748b' }}>Facilities & Amenities (Comma separated)</label>
                <textarea
                  rows="3"
                  placeholder="e.g. Medical Station, Generator, Clean Water, Modular Tents"
                  style={{ ...styles.input, resize: 'vertical' }}
                  value={formData.facilities}
                  onChange={(e) => setFormData({ ...formData, facilities: e.target.value })}
                />
              </div>
            </div>

            <div style={styles.drawerFooter}>
              <button
                onClick={() => setIsDrawerOpen(false)}
                style={{ padding: '8px 16px', borderRadius: '6px', border: '1px solid #cbd5e1', backgroundColor: '#fff', cursor: 'pointer', fontSize: '13px', color: '#334155' }}
              >
                Cancel
              </button>
              <button
                onClick={handleSave}
                style={{ padding: '8px 16px', borderRadius: '6px', border: 'none', backgroundColor: '#0d9488', color: '#fff', cursor: 'pointer', fontSize: '13px', fontWeight: '600' }}
              >
                {isEditing ? 'Save Changes' : 'Save Evacuation Center'}
              </button>
            </div>
          </div>
        </div>
      )}

    </div>
  );
}
