// App settings: API address, demo data, sign-in mode, account rules and the industry list
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const MIN_PASSWORD_LENGTH = 8;

const CONFIG = {
  API_BASE: "http://localhost:5080/api",

  USE_MOCK_USERS: false,
  USE_MOCK_SUBMISSIONS: false,
  USE_MOCK_FORECASTS: false,
  USE_MOCK_TARGETS: false,
  USE_MOCK_LOGIN: false,
  SHOW_DEMO_ACCOUNTS: true,
};

const INDUSTRIES = [
  "Retail",
  "Wholesale",
  "Manufacturing",
  "Construction",
  "Hospitality and tourism",
  "Transport and logistics",
  "Professional services",
  "Agriculture",
  "Health",
  "Education",
  "Information technology",
  "Personal services",
  "Other",
];
